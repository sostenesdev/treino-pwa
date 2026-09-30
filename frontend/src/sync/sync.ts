import { api, ApiError, token } from "../shared/api";
import type { Account, Bootstrap, Conflict, SyncPage } from "../shared/types";
import {
  acknowledge,
  acquireLease,
  activeAccount,
  applyChanges,
  markError,
  markSending,
  meta,
  pending,
  releaseLease,
  saveBootstrap,
  saveConflict,
} from "../offline/store";
export function deviceId() {
  let id = localStorage.getItem("treinos-device-id");
  if (!id) {
    id = crypto.randomUUID();
    localStorage.setItem("treinos-device-id", id);
  }
  return id;
}
export function networkType() {
  return (
    (navigator as Navigator & { connection?: { type?: string } }).connection
      ?.type || "unknown"
  );
}
export async function prepare(account: Account) {
  const me = await api<Account>("/auth/me");
  if (me.id !== account.id || me.mustChangePassword)
    throw Error("Entre com a mesma conta e altere a senha inicial.");
  const data = await api<Bootstrap>("/sync/bootstrap");
  await saveBootstrap(account.id, data);
  await navigator.storage?.persist?.();
  return data;
}
async function receive(account: Account) {
  let cursor = (await meta(account.id))?.cursor;
  if (cursor === undefined) {
    await prepare(account);
    return;
  }
  for (let pageCount = 0; pageCount < 100; pageCount++) {
    try {
      const page: SyncPage = await api<SyncPage>(
        `/sync/changes?after=${cursor}&limit=100`,
      );
      const reset = await applyChanges(account.id, page);
      if (reset) {
        await prepare(account);
        return;
      }
      cursor = page.nextCursor;
      if (!page.hasMore) return;
    } catch (e) {
      if (e instanceof ApiError && e.status === 410) {
        await prepare(account);
        return;
      }
      throw e;
    }
  }
}
export async function synchronize(
  account: Account,
  manual = false,
): Promise<string> {
  if (!manual && networkType() !== "wifi")
    return "Aguardando Wi-Fi ou confirmação manual.";
  const holder = crypto.randomUUID();
  if (!(await acquireLease(account.id, holder)))
    return "A sincronização já está em andamento em outra aba.";
  let done = 0;
  let blocked = 0;
  try {
    await api("/health/connectivity");
    const me = await api<Account>("/auth/me");
    if (me.id !== account.id || me.mustChangePassword)
      return "Entre novamente com a mesma conta e conclua a troca de senha.";
    await token();
    const operations = await pending(account.id);
    for (const original of operations.slice(0, 20)) {
      if ((await activeAccount())?.id !== account.id)
        return "A conta ativa mudou. A fila anterior foi preservada.";
      if (!(await acquireLease(account.id, holder)))
        return "A sincronização foi assumida por outra aba.";
      const latest = await pending(account.id);
      const candidate = latest.find(
        (x) => x.operationId === original.operationId,
      );
      if (!candidate) continue;
      if (
        latest.some(
          (x) =>
            x.entityId === candidate.entityId &&
            x.createdAt < candidate.createdAt,
        ) ||
        candidate.state === "conflict" ||
        candidate.state === "failed"
      ) {
        blocked++;
        continue;
      }
      if (!manual && (candidate.retryAt ?? 0) > Date.now()) continue;
      const verified = await api<Account>("/auth/me");
      if (verified.id !== account.id)
        return "Conta autenticada diferente. A fila foi preservada.";
      const op = await markSending(account.id, candidate);
      if (!op) continue;
      try {
        const payload = {
          operationId: op.operationId,
          deviceId: op.deviceId,
          schemaVersion: op.schemaVersion,
          entityId: op.entityId,
          kind: op.kind,
          baseVersion: op.baseVersion,
          snapshot: op.snapshot,
        };
        const ack = await api<{ version: number }>("/sync/operations", {
          method: "POST",
          body: JSON.stringify(payload),
        });
        await acknowledge(account.id, op, ack.version);
        done++;
      } catch (e) {
        const err = e as Error;
        if (
          e instanceof ApiError &&
          e.status === 409 &&
          e.data.code === "VERSION_CONFLICT"
        ) {
          await saveConflict(account.id, op, {
            entityId: op.entityId,
            version: Number(e.data.version),
            server: e.data.snapshot as Conflict["server"],
            deleted: Boolean(e.data.deleted),
          });
          blocked++;
          continue;
        }
        const status = e instanceof ApiError ? e.status : 0;
        const state =
          status === 401
            ? "auth_required"
            : status === 400 || status === 404
              ? "failed"
              : status === 409 || status === 403
                ? "failed"
                : "pending";
        const delay = Math.max(
          e instanceof ApiError ? e.retryAfterMs : 0,
          Math.min(300000, 1000 * 2 ** Math.min(op.attempts ?? 1, 8)) *
            (0.75 + Math.random() * 0.5),
        );
        await markError(account.id, op, state, err.message, Date.now() + delay);
        blocked++;
        if (
          status === 401 ||
          status === 403 ||
          status === 429 ||
          status === 0 ||
          status >= 500
        )
          break;
      }
    }
    await receive(account);
    return `${done} operação(ões) sincronizada(s).${blocked ? " Há pendências que precisam de atenção." : ""}`;
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) {
      for (const op of await pending(account.id))
        if (op.state !== "conflict" && op.state !== "failed")
          await markError(
            account.id,
            op,
            "auth_required",
            "Entre novamente com esta conta.",
          );
      return "Sessão expirada. Os treinos continuam salvos neste dispositivo.";
    }
    return "Não foi possível sincronizar. As pendências foram preservadas.";
  } finally {
    await releaseLease(account.id, holder);
  }
}
