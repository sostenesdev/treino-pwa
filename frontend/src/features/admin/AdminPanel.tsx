import {
  lazy,
  Suspense,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { api, ownerApi } from "../../shared/api";
import type { Account, Bootstrap, Session, UserDto } from "../../shared/types";
import { today } from "../../shared/date";
import { Workout } from "../sessions/Workout";
const Reports = lazy(() =>
  import("../reports/Reports").then((m) => ({ default: m.Reports })),
);
type ResourceRenderer = (
  data: Bootstrap | null,
  reload: () => Promise<void>,
  ownerId: string,
) => ReactNode;

function UserEditor({
  user,
  allowRole,
  onSaved,
}: {
  user?: UserDto;
  allowRole: boolean;
  onSaved: (user: UserDto) => Promise<void>;
}) {
  const [name, setName] = useState(user?.displayName || ""),
    [email, setEmail] = useState(user?.email || ""),
    [role, setRole] = useState<Account["role"]>(user?.role || "common"),
    [timeZone, setTimeZone] = useState(user?.timeZone || "America/Sao_Paulo"),
    [password, setPassword] = useState(""),
    [message, setMessage] = useState(""),
    [busy, setBusy] = useState(false);
  return (
    <form
      onSubmit={async (e) => {
        e.preventDefault();
        if (busy) return;
        setBusy(true);
        setMessage("");
        try {
          const saved = await api<UserDto>(
            "/users" + (user ? "/" + user.id : ""),
            {
              method: user ? "PUT" : "POST",
              body: JSON.stringify({
                name,
                email,
                role,
                timeZone,
                initialPassword: user ? undefined : password,
                expectedVersion: user?.rowVersion || 0,
              }),
            },
          );
          setPassword("");
          await onSaved(saved);
        } catch (error) {
          setMessage((error as Error).message);
        } finally {
          setBusy(false);
        }
      }}
    >
      <h3>{user ? "Editar usuário" : "Novo usuário"}</h3>
      <label>
        Nome do usuário
        <input
          required
          minLength={2}
          maxLength={120}
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
      </label>
      <label>
        E-mail do usuário
        <input
          required
          type="email"
          maxLength={256}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </label>
      {allowRole && (
        <label>
          Perfil do usuário
          <select
            value={role}
            onChange={(e) => setRole(e.target.value as Account["role"])}
          >
            <option value="common">Comum</option>
            <option value="administrator">Administrador</option>
          </select>
        </label>
      )}
      <label>
        Fuso horário
        <input
          required
          value={timeZone}
          onChange={(e) => setTimeZone(e.target.value)}
        />
      </label>
      {!user && (
        <>
          <label>
            Senha inicial
            <input
              required
              type="password"
              autoComplete="new-password"
              minLength={12}
              maxLength={128}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>
          <p>O usuário deverá trocar a senha no primeiro acesso.</p>
        </>
      )}
      {user && <p>Ao salvar, as sessões deste usuário serão encerradas.</p>}
      <button disabled={busy}>
        {busy ? "Salvando…" : user ? "Salvar usuário" : "Criar usuário"}
      </button>
      {message && (
        <p role="alert" className="error">
          {message}
        </p>
      )}
    </form>
  );
}

export function ProfileForm({
  account,
  onChanged,
}: {
  account: Account;
  onChanged: () => Promise<void>;
}) {
  const [user, setUser] = useState<UserDto>(),
    [message, setMessage] = useState("");
  useEffect(() => {
    let active = true;
    api<UserDto>("/users/" + account.id)
      .then((u) => {
        if (active) setUser(u);
      })
      .catch((e) => {
        if (active) setMessage(e.message);
      });
    return () => {
      active = false;
    };
  }, [account.id]);
  return (
    <details>
      <summary>Editar meu cadastro</summary>
      {user && (
        <UserEditor
          key={user.rowVersion}
          user={user}
          allowRole={false}
          onSaved={onChanged}
        />
      )}
      {message && <p role="alert">{message}</p>}
    </details>
  );
}

export function AdminPanel({
  actor,
  onOwnAccountChanged,
  renderResources,
}: {
  actor: Account;
  onOwnAccountChanged: () => Promise<void>;
  renderResources: ResourceRenderer;
}) {
  const [users, setUsers] = useState<UserDto[]>([]),
    [selectedId, setSelected] = useState(actor.id),
    [creating, setCreating] = useState(false),
    [message, setMessage] = useState(""),
    [busy, setBusy] = useState(false);
  async function reloadUsers() {
    const list = await api<UserDto[]>("/users");
    setUsers(list);
    return list;
  }
  useEffect(() => {
    let active = true;
    api<UserDto[]>("/users")
      .then((list) => {
        if (active) setUsers(list);
      })
      .catch((e) => {
        if (active) setMessage(e.message);
      });
    return () => {
      active = false;
    };
  }, []);
  const selected = users.find((u) => u.id === selectedId);
  async function saved(user: UserDto) {
    if (!creating && user.id === actor.id) {
      await onOwnAccountChanged();
      return;
    }
    await reloadUsers();
    setSelected(user.id);
    setCreating(false);
    setMessage("Usuário salvo.");
  }
  async function remove() {
    if (
      !selected ||
      busy ||
      !confirm(
        `Excluir o usuário ${selected.displayName}? Seus registros históricos serão preservados e o acesso será bloqueado.`,
      )
    )
      return;
    setBusy(true);
    try {
      await api(
        `/users/${selected.id}?expectedVersion=${selected.rowVersion}`,
        { method: "DELETE" },
      );
      if (selected.id === actor.id) {
        await onOwnAccountChanged();
        return;
      }
      await reloadUsers();
      setSelected(actor.id);
      setMessage("Usuário excluído.");
    } catch (error) {
      setMessage((error as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="admin-panel">
      <section className="card">
        <h2>Usuários</h2>
        <label>
          Usuário para gerenciar
          <select
            value={selectedId}
            onChange={(e) => {
              setSelected(e.target.value);
              setCreating(false);
              setMessage("");
            }}
          >
            {users.map((u) => (
              <option key={u.id} value={u.id}>
                {u.displayName} · {u.email}
              </option>
            ))}
          </select>
        </label>
        <div className="actions">
          <button className="secondary" onClick={() => setCreating(true)}>
            Novo usuário
          </button>
          <button
            className="secondary"
            onClick={() => reloadUsers().catch((e) => setMessage(e.message))}
          >
            Atualizar usuários
          </button>
        </div>
        {creating ? (
          <UserEditor key="new" allowRole onSaved={saved} />
        ) : (
          selected && (
            <>
              <UserEditor
                key={selected.id + ":" + selected.rowVersion}
                user={selected}
                allowRole
                onSaved={saved}
              />
              <button className="danger" disabled={busy} onClick={remove}>
                Excluir usuário
              </button>
            </>
          )
        )}
        {message && <p role="status">{message}</p>}
      </section>
      {selected && (
        <OwnerResources
          key={selected.id}
          user={selected}
          renderResources={renderResources}
        />
      )}
    </div>
  );
}

// Other accounts are managed online; their data never enters the actor's offline store.
function OwnerResources({
  user,
  renderResources,
}: {
  user: UserDto;
  renderResources: ResourceRenderer;
}) {
  const [data, setData] = useState<Bootstrap | null>(null),
    [draft, setDraft] = useState<Session | null>(null),
    [message, setMessage] = useState(""),
    [history, setHistory] = useState<Session[]>([]),
    [from, setFrom] = useState(() => {
      const d = new Date();
      d.setDate(d.getDate() - 90);
      return d.toISOString().slice(0, 10);
    }),
    [to, setTo] = useState(today(user.timeZone)),
    [page, setPage] = useState(1),
    [saving, setSaving] = useState(false);
  const pending = useRef<{ json: string; operation: object } | null>(null);
  async function loadHistory(requestedPage = page) {
    const rows = await ownerApi<Session[]>(
      user.id,
      `/sessions?from=${from}&to=${to}&page=${requestedPage}`,
    );
    setHistory(rows);
    setPage(requestedPage);
  }
  async function reload() {
    setData(await ownerApi<Bootstrap>(user.id, "/sync/bootstrap"));
    await loadHistory();
  }
  useEffect(() => {
    let active = true;
    ownerApi<Bootstrap>(user.id, "/sync/bootstrap")
      .then((b) => {
        if (active) {
          setData(b);
          setHistory(b.sessions);
        }
      })
      .catch((e) => {
        if (active) setMessage(e.message);
      });
    return () => {
      active = false;
    };
  }, [user.id]);
  async function save(session: Session) {
    if (saving) return false;
    setSaving(true);
    try {
      const json = JSON.stringify(session);
      if (pending.current?.json !== json)
        pending.current = {
          json,
          operation: {
            operationId: crypto.randomUUID(),
            deviceId: crypto.randomUUID(),
            schemaVersion: 1,
            entityId: session.id,
            kind: session.deleted
              ? "delete"
              : session.rowVersion
                ? "replace"
                : "create",
            baseVersion: session.rowVersion,
            snapshot: session.deleted ? null : session,
          },
        };
      const ack = await ownerApi<{ version: number }>(
        user.id,
        "/sync/operations",
        { method: "POST", body: JSON.stringify(pending.current.operation) },
      );
      pending.current = null;
      setDraft((previous) =>
        previous?.id === session.id
          ? session.deleted
            ? null
            : { ...session, rowVersion: ack.version }
          : previous,
      );
      setMessage("Registro salvo.");
      try {
        await reload();
      } catch {
        setMessage(
          "Registro salvo. Atualize a lista quando a conexão estiver disponível.",
        );
      }
      return true;
    } catch (error) {
      setMessage((error as Error).message);
      return false;
    } finally {
      setSaving(false);
    }
  }
  return (
    <>
      <h2>Dados de {user.displayName}</h2>
      <p>A edição dos dados de outros usuários requer conexão.</p>
      {message && <p role="status">{message}</p>}
      {data ? (
        <>
          {renderResources(data, reload, user.id)}
          <section className="card">
            <h2>Registros de execução</h2>
            <div className="compact-fields">
              <label>
                Histórico de
                <input
                  type="date"
                  value={from}
                  onChange={(e) => setFrom(e.target.value)}
                />
              </label>
              <label>
                Histórico até
                <input
                  type="date"
                  value={to}
                  onChange={(e) => setTo(e.target.value)}
                />
              </label>
              <button
                onClick={() =>
                  loadHistory(1).catch((e) => setMessage(e.message))
                }
              >
                Consultar registros
              </button>
            </div>
            {history.map((s) => (
              <div className="list-row" key={s.id}>
                <div>
                  <strong>{s.workoutNameSnapshot}</strong>
                  <small>
                    {s.performedOn} · {s.status}
                  </small>
                </div>
                <div className="actions">
                  <button
                    disabled={saving}
                    className="secondary small"
                    onClick={() => setDraft(s)}
                  >
                    Editar registro
                  </button>
                  <button
                    disabled={saving}
                    className="danger small"
                    onClick={() => {
                      if (confirm("Excluir este registro?"))
                        void save({ ...s, deleted: true });
                    }}
                  >
                    Excluir registro
                  </button>
                </div>
              </div>
            ))}
            {!history.length && <p>Nenhum registro neste período.</p>}
            <div className="actions">
              <button
                disabled={page <= 1}
                onClick={() =>
                  loadHistory(page - 1).catch((e) => setMessage(e.message))
                }
              >
                Página anterior
              </button>
              <span>Página {page}</span>
              <button
                disabled={!history.length}
                onClick={() =>
                  loadHistory(page + 1).catch((e) => setMessage(e.message))
                }
              >
                Próxima página
              </button>
            </div>
          </section>
          <fieldset disabled={saving}>
            <legend>Registrar ou editar execução</legend>
            <Workout
              key={user.id}
              timeZone={user.timeZone}
              data={data}
              draft={draft}
              history={history}
              onDraft={setDraft}
              onSave={save}
            />
          </fieldset>
          <Suspense fallback={<p>Carregando relatórios…</p>}>
            <Reports
              accountId={user.id}
              ownerId={user.id}
              history={history}
              data={data}
            />
          </Suspense>
        </>
      ) : (
        <p>Carregando treinos…</p>
      )}
    </>
  );
}
