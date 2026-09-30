import { useEffect, useState } from "react";
import type { Account, Conflict, Operation, Session } from "../../shared/types";
import {
  clearDevice,
  conflicts,
  meta,
  pending,
  resolveConflict,
  sessions,
} from "../../offline/store";
import { deviceId } from "../../sync/sync";
export function SyncPanel({
  account,
  onRefresh,
  onSync,
  onLogin,
}: {
  account: Account;
  onRefresh: () => Promise<void>;
  onSync: () => Promise<void>;
  onLogin: () => Promise<void>;
}) {
  const [queue, setQueue] = useState<Operation[]>([]),
    [issues, setIssues] = useState<Conflict[]>([]),
    [local, setLocal] = useState<Session[]>([]),
    [last, setLast] = useState(""),
    [message, setMessage] = useState("");
  async function load() {
    setQueue(await pending(account.id));
    setIssues(await conflicts(account.id));
    setLocal(await sessions(account.id));
    setLast((await meta(account.id))?.lastSync || "");
  }
  useEffect(() => {
    load();
  }, [account.id]);
  async function choose(c: Conflict, choice: "server" | "local" | "copy") {
    try {
      await resolveConflict(account.id, c.entityId, choice, deviceId());
      await load();
      await onRefresh();
      setMessage("Escolha salva neste dispositivo.");
    } catch (e) {
      setMessage((e as Error).message);
    }
  }
  return (
    <div className="password-box">
      <h3>Offline e sincronização</h3>
      <p>
        Última sincronização:{" "}
        {last ? new Date(last).toLocaleString("pt-BR") : "ainda não realizada"}
      </p>
      <p>
        Sincronização automática quando o navegador identifica Wi-Fi. Em redes
        não identificadas, confirme a conexão para sincronizar.
      </p>
      <button
        className="secondary"
        onClick={async () => {
          await onSync();
          await load();
        }}
      >
        Estou no Wi-Fi — sincronizar agora
      </button>
      {message && <p role="status">{message}</p>}
      {queue.map((op) => (
        <p key={op.operationId}>
          {local.find((s) => s.id === op.entityId)?.workoutNameSnapshot ||
            "Treino"}
          :{" "}
          {op.state === "auth_required"
            ? "entre novamente"
            : op.state === "conflict"
              ? "conflito de versões"
              : op.state === "failed"
                ? "precisa de correção"
                : op.state === "sending"
                  ? "enviando"
                  : "aguardando envio"}
          {op.error ? " · " + op.error : ""}
        </p>
      ))}
      {queue.some((o) => o.state === "auth_required") && (
        <button onClick={onLogin}>Entrar novamente nesta conta</button>
      )}
      {issues.map((c) => {
        const own = local.find((s) => s.id === c.entityId);
        return (
          <div className="card conflict" key={c.entityId}>
            <h3>Revisar versões do treino</h3>
            <div className="grid">
              <div>
                <h4>Neste dispositivo</h4>
                <Comparison session={own} />
              </div>
              <div>
                <h4>No servidor</h4>
                {c.deleted ? (
                  <p>Treino excluído.</p>
                ) : (
                  <Comparison session={c.server || undefined} />
                )}
              </div>
            </div>
            <div className="actions">
              <button className="secondary" onClick={() => choose(c, "server")}>
                Adotar versão do servidor
              </button>
              {!c.deleted && (
                <button onClick={() => choose(c, "local")}>
                  Reaplicar minha edição após revisão
                </button>
              )}
              {c.deleted && own && (
                <button onClick={() => choose(c, "copy")}>
                  Preservar como um novo treino
                </button>
              )}
            </div>
          </div>
        );
      })}
      <button
        className="danger"
        onClick={async () => {
          if (
            !confirm(
              queue.length
                ? "Há treinos ainda não sincronizados. Apagar os dados desta conta neste dispositivo?"
                : "Apagar os dados desta conta neste dispositivo?",
            )
          )
            return;
          await clearDevice(account.id);
          await onLogin();
        }}
      >
        Apagar dados deste dispositivo
      </button>
    </div>
  );
}
function Comparison({ session }: { session: Session | undefined }) {
  if (!session) return <p>Cópia local marcada para exclusão.</p>;
  return (
    <>
      <strong>{session.workoutNameSnapshot}</strong>
      <p>
        {session.performedOn} ·{" "}
        {session.status === "completed" ? "Concluído" : "Em andamento"}
      </p>
      {session.exercises.map((e) => (
        <div key={e.id}>
          <strong>{e.exerciseNameSnapshot}</strong>
          <small>
            {e.completionStatus === "completed"
              ? "Realizado"
              : e.completionStatus === "skipped"
                ? "Pulado"
                : "Planejado"}
          </small>
          {e.sets.map((s) => (
            <small key={s.id}>
              Série {s.setNumber}: {s.repetitions ?? s.durationSeconds}{" "}
              {s.repetitions !== null ? "reps" : "s"} ·{" "}
              {s.loadKg === null ? "carga não informada" : s.loadKg + " kg"}
            </small>
          ))}
        </div>
      ))}
      {session.notes && <p>{session.notes}</p>}
    </>
  );
}
