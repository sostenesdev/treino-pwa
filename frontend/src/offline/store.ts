import { openDB } from "idb";
import type {
  Account,
  Bootstrap,
  Conflict,
  Operation,
  Session,
  SyncPage,
} from "../shared/types";
const db = openDB("treinos-v1", 2, {
  upgrade(db, old) {
    if (old < 1) {
      for (const name of [
        "accounts",
        "snapshots",
        "sessions",
        "outbox",
        "meta",
      ])
        db.createObjectStore(name);
    }
    if (old < 2) db.createObjectStore("conflicts");
  },
});
const key = (user: string, id: string) => `${user}:${id}`;
const opKey = (user: string, op: Operation) =>
  `${user}:${op.entityId}:${op.operationId}`;
const owned = (all: Operation[], user: string, id?: string) =>
  all
    .filter((o) => o.owner === user && (!id || o.entityId === id))
    .sort((a, b) => a.createdAt - b.createdAt);
export async function saveAccount(account: Account) {
  const d = await db;
  const tx = d.transaction(["accounts", "meta"], "readwrite");
  await tx.objectStore("accounts").put(account, account.id);
  await tx.objectStore("meta").put(account.id, "active-account");
  await tx.done;
  sessionStorage.setItem("activeUser", account.id);
}
export async function activeAccount(): Promise<Account | null> {
  const d = await db;
  const id =
    sessionStorage.getItem("activeUser") ||
    (await d.get("meta", "active-account"));
  return id ? (await d.get("accounts", id)) || null : null;
}
export async function lockAccount() {
  sessionStorage.removeItem("activeUser");
  await (await db).delete("meta", "active-account");
}
export async function snapshot(user: string): Promise<Bootstrap | null> {
  return (await (await db).get("snapshots", user)) || null;
}
export async function meta(
  user: string,
): Promise<{ cursor: number; preparedAt: string; lastSync?: string } | null> {
  return (await (await db).get("meta", user)) || null;
}
export async function sessions(user: string): Promise<Session[]> {
  const d = await db;
  const keys = await d.getAllKeys("sessions");
  return (
    await Promise.all(
      keys
        .filter((k) => String(k).startsWith(user + ":"))
        .map((k) => d.get("sessions", k)),
    )
  )
    .filter((x): x is Session => !!x && !x.deleted)
    .sort((a, b) => b.performedOn.localeCompare(a.performedOn));
}
export async function pending(user: string): Promise<Operation[]> {
  return owned(await (await db).getAll("outbox"), user);
}
export async function conflicts(user: string): Promise<Conflict[]> {
  const d = await db;
  const keys = await d.getAllKeys("conflicts");
  return Promise.all(
    keys
      .filter((k) => String(k).startsWith(user + ":"))
      .map((k) => d.get("conflicts", k)),
  );
}
export async function saveBootstrap(user: string, data: Bootstrap) {
  const d = await db;
  const tx = d.transaction(
    ["snapshots", "sessions", "meta", "outbox", "conflicts"],
    "readwrite",
  );
  await tx.objectStore("snapshots").put(data, user);
  const previous = await tx.objectStore("meta").get(user);
  await tx.objectStore("meta").put(
    {
      ...previous,
      cursor: data.revision,
      preparedAt: new Date().toISOString(),
    },
    user,
  );
  const ops = owned(await tx.objectStore("outbox").getAll(), user);
  const serverIds = new Set(data.sessions.map((s) => s.id));
  for (const s of data.sessions) {
    const head = ops.find((o) => o.entityId === s.id);
    if (!head) await tx.objectStore("sessions").put(s, key(user, s.id));
    else if (s.rowVersion > head.baseVersion && head.kind !== "create") {
      await tx
        .objectStore("conflicts")
        .put(
          { entityId: s.id, version: s.rowVersion, server: s, deleted: false },
          key(user, s.id),
        );
      await tx
        .objectStore("outbox")
        .put({ ...head, state: "conflict" }, opKey(user, head));
    }
  }
  const ids = await tx.objectStore("sessions").getAllKeys();
  const from = new Date();
  from.setUTCDate(from.getUTCDate() - data.historyDays);
  const boundary = from.toISOString().slice(0, 10);
  for (const id of ids.filter((k) => String(k).startsWith(user + ":"))) {
    const s: Session = await tx.objectStore("sessions").get(id);
    if (!serverIds.has(s.id) && s.rowVersion > 0 && s.performedOn >= boundary) {
      const head = ops.find((o) => o.entityId === s.id);
      if (!head) await tx.objectStore("sessions").delete(id);
      else if (head.kind !== "delete") {
        await tx.objectStore("conflicts").put(
          {
            entityId: s.id,
            version: s.rowVersion,
            server: null,
            deleted: true,
          },
          key(user, s.id),
        );
        await tx
          .objectStore("outbox")
          .put({ ...head, state: "conflict" }, opKey(user, head));
      }
    }
  }
  await tx.done;
}
export async function saveSession(
  user: string,
  session: Session,
  deviceId: string,
) {
  const d = await db;
  const tx = d.transaction(["sessions", "outbox"], "readwrite");
  const k = key(user, session.id);
  const ops = owned(await tx.objectStore("outbox").getAll(), user, session.id);
  const last = ops.at(-1);
  const stored: Session | undefined = await tx.objectStore("sessions").get(k);
  const version = Math.max(session.rowVersion, stored?.rowVersion ?? 0);
  session = { ...session, rowVersion: version };
  if (
    session.deleted &&
    version === 0 &&
    ops.length === 1 &&
    !ops[0].attempted
  ) {
    await tx.objectStore("sessions").delete(k);
    await tx.objectStore("outbox").delete(opKey(user, ops[0]));
    await tx.done;
    return;
  }
  const kind = session.deleted
    ? "delete"
    : version === 0 && !ops.length
      ? "create"
      : "replace";
  const op: Operation = {
    operationId: crypto.randomUUID(),
    deviceId,
    schemaVersion: 1,
    entityId: session.id,
    kind,
    baseVersion: version,
    snapshot: session.deleted ? null : session,
    state: "pending",
    attempted: false,
    owner: user,
    createdAt: Math.max(Date.now(), (last?.createdAt ?? 0) + 1),
  };
  if (last && !last.attempted) {
    await tx.objectStore("outbox").delete(opKey(user, last));
    op.kind = last.kind === "create" && !session.deleted ? "create" : kind;
    op.baseVersion = last.baseVersion;
    op.createdAt = last.createdAt;
    op.state = last.state === "conflict" ? "conflict" : "pending";
  }
  await tx.objectStore("sessions").put(session, k);
  await tx.objectStore("outbox").put(op, opKey(user, op));
  await tx.done;
}
export async function markSending(
  user: string,
  op: Operation,
): Promise<Operation | null> {
  const d = await db;
  const tx = d.transaction("outbox", "readwrite");
  const k = opKey(user, op);
  const current: Operation | undefined = await tx.store.get(k);
  const frozen = current
    ? {
        ...current,
        state: "sending" as const,
        attempted: true,
        attempts: (current.attempts ?? 0) + 1,
      }
    : null;
  if (frozen) await tx.store.put(frozen, k);
  await tx.done;
  return frozen;
}
export async function markError(
  user: string,
  op: Operation,
  state: Operation["state"],
  error: string,
  retryAt = 0,
) {
  const d = await db;
  const tx = d.transaction("outbox", "readwrite");
  const k = opKey(user, op);
  const current = await tx.store.get(k);
  if (current) await tx.store.put({ ...current, state, error, retryAt }, k);
  await tx.done;
}
export async function saveConflict(
  user: string,
  op: Operation,
  conflict: Conflict,
) {
  const d = await db;
  const tx = d.transaction(["outbox", "conflicts"], "readwrite");
  await tx.objectStore("conflicts").put(conflict, key(user, op.entityId));
  const current = await tx.objectStore("outbox").get(opKey(user, op));
  if (current)
    await tx
      .objectStore("outbox")
      .put(
        { ...current, state: "conflict", error: "Versão divergente" },
        opKey(user, op),
      );
  await tx.done;
}
export async function acknowledge(
  user: string,
  op: Operation,
  version: number,
) {
  const d = await db;
  const tx = d.transaction(["outbox", "sessions"], "readwrite");
  await tx.objectStore("outbox").delete(opKey(user, op));
  const next = owned(
    await tx.objectStore("outbox").getAll(),
    user,
    op.entityId,
  )[0];
  if (next)
    await tx.objectStore("outbox").put(
      {
        ...next,
        baseVersion: version,
        kind: next.kind === "create" ? "replace" : next.kind,
      },
      opKey(user, next),
    );
  const k = key(user, op.entityId);
  const s = await tx.objectStore("sessions").get(k);
  if (s) await tx.objectStore("sessions").put({ ...s, rowVersion: version }, k);
  await tx.done;
}
export async function resolveConflict(
  user: string,
  id: string,
  choice: "server" | "local" | "copy",
  deviceId: string,
) {
  const d = await db;
  const tx = d.transaction(["conflicts", "outbox", "sessions"], "readwrite");
  const k = key(user, id);
  const conflict: Conflict | undefined = await tx
    .objectStore("conflicts")
    .get(k);
  if (!conflict) throw Error("Conflito não encontrado.");
  if (choice === "local" && conflict.deleted) {
    await tx.done;
    throw Error(
      "O treino foi excluído no servidor. Preserve a cópia como um novo treino após revisão.",
    );
  }
  const local: Session = await tx.objectStore("sessions").get(k);
  const ops = owned(await tx.objectStore("outbox").getAll(), user, id);
  for (const op of ops) await tx.objectStore("outbox").delete(opKey(user, op));
  if (choice === "server") {
    if (conflict.deleted || !conflict.server)
      await tx.objectStore("sessions").delete(k);
    else await tx.objectStore("sessions").put(conflict.server, k);
  } else if (choice === "copy") {
    const copied: Session = {
      ...local,
      id: crypto.randomUUID(),
      rowVersion: 0,
      deleted: false,
      exercises: local.exercises.map((e) => ({
        ...e,
        id: crypto.randomUUID(),
        sets: e.sets.map((s) => ({ ...s, id: crypto.randomUUID() })),
      })),
    };
    const op: Operation = {
      operationId: crypto.randomUUID(),
      deviceId,
      schemaVersion: 1,
      entityId: copied.id,
      kind: "create",
      baseVersion: 0,
      snapshot: copied,
      state: "pending",
      attempted: false,
      owner: user,
      createdAt: Date.now(),
    };
    await tx.objectStore("sessions").delete(k);
    await tx.objectStore("sessions").put(copied, key(user, copied.id));
    await tx.objectStore("outbox").put(op, opKey(user, op));
  } else {
    if (conflict.deleted)
      throw Error(
        "O treino foi excluído no servidor. Adote a exclusão ou registre um novo treino após revisar a cópia local.",
      );
    const updated = { ...local, rowVersion: conflict.version };
    const op: Operation = {
      operationId: crypto.randomUUID(),
      deviceId,
      schemaVersion: 1,
      entityId: id,
      kind: local.deleted ? "delete" : "replace",
      baseVersion: conflict.version,
      snapshot: local.deleted ? null : updated,
      state: "pending",
      attempted: false,
      owner: user,
      createdAt: Date.now(),
    };
    await tx.objectStore("sessions").put(updated, k);
    await tx.objectStore("outbox").put(op, opKey(user, op));
  }
  await tx.objectStore("conflicts").delete(k);
  await tx.done;
}
export async function applyChanges(user: string, page: SyncPage) {
  const d = await db;
  const tx = d.transaction(
    ["sessions", "snapshots", "meta", "outbox", "conflicts"],
    "readwrite",
  );
  const ops = owned(await tx.objectStore("outbox").getAll(), user);
  let reset = false;
  for (const c of page.changes) {
    if (
      c.changeKind === "reset_required" ||
      c.entityType === "plan" ||
      c.entityType === "template"
    ) {
      reset = true;
      continue;
    }
    if (!c.entityId) continue;
    if (c.entityType === "session") {
      const s: Session | null = c.payloadJson
        ? JSON.parse(c.payloadJson)
        : null;
      const head = ops.find((o) => o.entityId === c.entityId);
      const k = key(user, c.entityId);
      const local: Session | undefined = await tx
        .objectStore("sessions")
        .get(k);
      if (head) {
        if (
          c.changeKind === "delete" ||
          (s && s.rowVersion > head.baseVersion)
        ) {
          await tx.objectStore("conflicts").put(
            {
              entityId: c.entityId,
              version:
                s?.rowVersion ??
                c.entityVersion ??
                (local?.rowVersion ?? 0) + 1,
              server: s,
              deleted: c.changeKind === "delete",
            },
            k,
          );
          await tx
            .objectStore("outbox")
            .put({ ...head, state: "conflict" }, opKey(user, head));
        }
      } else if (c.changeKind === "delete")
        await tx.objectStore("sessions").delete(k);
      else if (s) await tx.objectStore("sessions").put(s, k);
    } else if (c.entityType === "exercise") {
      const snap: Bootstrap | undefined = await tx
        .objectStore("snapshots")
        .get(user);
      if (snap) {
        snap.exercises = snap.exercises.filter((x) => x.id !== c.entityId);
        if (c.payloadJson && c.changeKind === "upsert")
          snap.exercises.push(JSON.parse(c.payloadJson));
        await tx.objectStore("snapshots").put(snap, user);
      }
    }
  }
  const old = await tx.objectStore("meta").get(user);
  await tx
    .objectStore("meta")
    .put(
      { ...old, cursor: page.nextCursor, lastSync: new Date().toISOString() },
      user,
    );
  await tx.done;
  return reset;
}
export async function acquireLease(user: string, holder: string) {
  const d = await db;
  const tx = d.transaction("meta", "readwrite");
  const k = "lease:" + user;
  const current = await tx.store.get(k);
  if (current && current.holder !== holder && current.expiresAt > Date.now()) {
    await tx.done;
    return false;
  }
  await tx.store.put({ holder, expiresAt: Date.now() + 120000 }, k);
  await tx.done;
  return true;
}
export async function releaseLease(user: string, holder: string) {
  const d = await db;
  const tx = d.transaction("meta", "readwrite");
  const k = "lease:" + user;
  const current = await tx.store.get(k);
  if (current?.holder === holder) await tx.store.delete(k);
  await tx.done;
}
export async function clearDevice(user: string) {
  const d = await db;
  const tx = d.transaction(
    ["accounts", "snapshots", "meta", "sessions", "outbox", "conflicts"],
    "readwrite",
  );
  for (const name of ["sessions", "outbox", "conflicts"] as const) {
    const store = tx.objectStore(name);
    for (const k of await store.getAllKeys())
      if (String(k).startsWith(user + ":")) await store.delete(k);
  }
  for (const name of ["accounts", "snapshots", "meta"] as const)
    await tx.objectStore(name).delete(user);
  await tx.done;
  await lockAccount();
}
