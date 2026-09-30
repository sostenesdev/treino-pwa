import React, { lazy, Suspense, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { api, token } from "./shared/api";
import {
  activeAccount,
  lockAccount,
  meta,
  pending,
  saveAccount,
  saveSession,
  sessions,
  snapshot,
} from "./offline/store";
import { deviceId, networkType, prepare, synchronize } from "./sync/sync";
import type {
  Account,
  Bootstrap,
  Exercise,
  ExerciseEntry,
  Plan,
  Session,
  SetEntry,
  Template,
} from "./shared/types";
import "./style.css";
import {
  QueryClient,
  QueryClientProvider,
  useQueryClient,
} from "@tanstack/react-query";
import { BrowserRouter, useNavigate, useLocation } from "react-router-dom";
const Reports = lazy(() =>
  import("./features/reports/Reports").then((m) => ({ default: m.Reports })),
);
import { UpdatePrompt } from "./features/sync/UpdatePrompt";
import { PlansManager } from "./features/plans/PlansManager";
import { Workout } from "./features/sessions/Workout";
import { SyncPanel } from "./features/sync/SyncPanel";
function App() {
  const queryClient = useQueryClient();
  const navigate = useNavigate(),
    location = useLocation();
  type Tab = "home" | "workout" | "history" | "catalog" | "reports" | "account";
  const tab: Tab =
    location.pathname === "/" ? "home" : (location.pathname.slice(1) as Tab);
  const setTab = (value: Tab) => navigate(value === "home" ? "/" : "/" + value);
  const [account, setAccount] = useState<Account | null>(null),
    [data, setData] = useState<Bootstrap | null>(null),
    [history, setHistory] = useState<Session[]>([]),
    [queue, setQueue] = useState(0),
    [notice, setNotice] = useState(""),
    [draft, setDraft] = useState<Session | null>(null),
    [ready, setReady] = useState(false);
  async function refresh(a = account) {
    if (!a) return;
    setData(await snapshot(a.id));
    setHistory(await sessions(a.id));
    setQueue((await pending(a.id)).length);
  }
  useEffect(() => {
    activeAccount().then(async (a) => {
      if (a) {
        setAccount(a);
        await refresh(a);
        if (navigator.onLine) {
          api<Account>("/auth/me")
            .then((me) => {
              if (me.id === a.id) {
                saveAccount(me);
                setAccount(me);
                synchronize(me)
                  .then(setNotice)
                  .then(() => refresh(me));
              }
            })
            .catch(() => {});
        }
      }
      setReady(true);
    });
  }, []);
  async function login(email: string, password: string) {
    await token();
    const a = await api<Account>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
    await token();
    await saveAccount(a);
    setAccount(a);
    await refresh(a);
    setNotice("Sessão iniciada.");
  }
  async function logout() {
    try {
      await api("/auth/logout", { method: "POST" });
    } catch {}
    await lockAccount();
    queryClient.clear();
    setHistory([]);
    setQueue(0);
    setAccount(null);
    setData(null);
    setDraft(null);
    setNotice("Dados locais preservados para esta conta.");
  }
  async function save(s: Session) {
    if (!account) return false;
    try {
      await saveSession(account.id, s, deviceId());
      if (s.deleted && draft?.id === s.id) setDraft(null);
      await refresh(account);
      setNotice("Salvo neste dispositivo.");
      return true;
    } catch (e) {
      setNotice("Não salvo: " + (e as Error).message);
      return false;
    }
  }
  async function sync(manual = true) {
    if (!account) return;
    setNotice("Sincronizando…");
    setNotice(await synchronize(account, manual));
    await refresh(account);
  }
  async function prep() {
    if (!account) return;
    try {
      setNotice("Preparando dados…");
      const b = await prepare(account);
      setData(b);
      await refresh(account);
      setNotice(`Offline preparado: ${b.historyDays} dias de histórico.`);
    } catch (e) {
      setNotice("Falha ao preparar: " + (e as Error).message);
    }
  }
  useEffect(() => {
    if (!account || account.mustChangePassword) return;
    const run = () => {
      synchronize(account)
        .then(setNotice)
        .then(() => refresh(account));
    };
    const visible = () => {
      if (document.visibilityState === "visible") run();
    };
    window.addEventListener("online", run);
    window.addEventListener("focus", run);
    document.addEventListener("visibilitychange", visible);
    return () => {
      window.removeEventListener("online", run);
      window.removeEventListener("focus", run);
      document.removeEventListener("visibilitychange", visible);
    };
  }, [account?.id]);
  if (location.pathname === "/reset-password") return <ResetPassword />;
  if (!ready)
    return (
      <main className="shell">
        <p>Carregando…</p>
      </main>
    );
  if (!account) return <Login onLogin={login} notice={notice} />;
  return (
    <div className="app">
      <header>
        <div className="brand">
          <span className="brand-icon">◆</span>
          <span>Treinos</span>
        </div>
        <div className="header-right">
          <span className="network">
            {navigator.onLine ? "Online" : "Offline"} · {networkType()}
          </span>
          <button className="text" onClick={() => setTab("account")}>
            {account.displayName}
          </button>
        </div>
      </header>
      <UpdatePrompt />
      <main className="shell">
        <div className="heading">
          <div>
            <span className="eyebrow">SEU ESPAÇO DE TREINO</span>
            <h1>
              {tab === "home"
                ? "Vamos treinar?"
                : tab === "workout"
                  ? "Registrar treino"
                  : tab === "history"
                    ? "Histórico"
                    : tab === "catalog"
                      ? "Exercícios e fichas"
                      : tab === "reports"
                        ? "Progressão"
                        : "Sua conta"}
            </h1>
          </div>
          <span className="sync-pill">
            {queue} pendência{queue === 1 ? "" : "s"}
          </span>
        </div>
        {notice && (
          <div role="status" className="notice">
            {notice}
          </div>
        )}
        {account.mustChangePassword ? (
          <ChangePassword onChanged={() => logout()} />
        ) : (
          <>
            {tab === "home" && (
              <section className="grid">
                <div className="card hero">
                  <span className="eyebrow">PRONTO PARA A PRÓXIMA SÉRIE</span>
                  <h2>Seu treino acompanha você.</h2>
                  <p>
                    Registre na academia, mesmo sem internet. Seus dados ficam
                    neste dispositivo até sincronizar.
                  </p>
                  <div className="actions">
                    <button
                      onClick={() => {
                        setTab("workout");
                        setDraft(null);
                      }}
                    >
                      Começar treino
                    </button>
                    <button className="secondary" onClick={prep}>
                      Preparar offline
                    </button>
                  </div>
                </div>
                <div className="card">
                  <span className="eyebrow">SINCRONIZAÇÃO</span>
                  <h3>
                    {queue ? `${queue} treino(s) aguardando` : "Tudo em dia"}
                  </h3>
                  <p>
                    {data
                      ? "Catálogo e fichas disponíveis offline."
                      : "Prepare este dispositivo enquanto estiver online."}
                  </p>
                  <button className="secondary" onClick={() => sync(true)}>
                    Estou no Wi-Fi — sincronizar
                  </button>
                </div>
                <div className="card">
                  <span className="eyebrow">RECENTES</span>
                  {history.slice(0, 3).map((s) => (
                    <div className="list-row" key={s.id}>
                      <strong>{s.workoutNameSnapshot}</strong>
                      <span>
                        {s.performedOn} ·{" "}
                        {s.status === "completed"
                          ? "Concluído"
                          : "Em andamento"}
                      </span>
                    </div>
                  ))}
                  {!history.length && <p>Nenhum treino registrado ainda.</p>}
                </div>
              </section>
            )}
            {tab === "workout" && (
              <Workout
                timeZone={account.timeZone}
                data={data}
                draft={draft}
                history={history}
                onDraft={setDraft}
                onSave={save}
              />
            )}
            {tab === "history" && (
              <section className="card">
                {history.length ? (
                  history.map((s) => (
                    <div className="list-row" key={s.id}>
                      <div>
                        <strong>{s.workoutNameSnapshot}</strong>
                        <small>
                          {s.performedOn} ·{" "}
                          {s.status === "completed"
                            ? "Concluído"
                            : "Em andamento"}{" "}
                          ·{" "}
                          {
                            s.exercises.filter(
                              (e) => e.completionStatus === "completed",
                            ).length
                          }{" "}
                          exercícios
                        </small>
                      </div>
                      <div className="actions">
                        <button
                          className="secondary small"
                          onClick={() => {
                            setDraft(s);
                            setTab("workout");
                          }}
                        >
                          Editar
                        </button>
                        <button
                          className="danger small"
                          onClick={() => {
                            if (confirm("Excluir este treino?"))
                              save({ ...s, deleted: true });
                          }}
                        >
                          Excluir
                        </button>
                      </div>
                    </div>
                  ))
                ) : (
                  <p>Seu histórico aparece aqui após registrar um treino.</p>
                )}
              </section>
            )}
            {tab === "catalog" && <Catalog data={data} onReload={prep} />}
            {tab === "reports" && (
              <Suspense fallback={<p>Carregando relatórios…</p>}>
                <Reports accountId={account.id} history={history} data={data} />
              </Suspense>
            )}
            {tab === "account" && (
              <section className="card">
                <h2>{account.displayName}</h2>
                <p>{account.email}</p>
                <p>
                  Perfil:{" "}
                  {account.role === "administrator" ? "Administrador" : "Comum"}
                </p>
                <p>
                  Dados offline: {data ? "preparados" : "ainda não preparados"}
                </p>
                <p>Pendências: {queue}</p>
                <button onClick={prep}>Preparar offline</button>{" "}
                <button className="secondary" onClick={() => sync(true)}>
                  Sincronizar agora
                </button>{" "}
                <button className="text" onClick={logout}>
                  Sair
                </button>
                <SyncPanel
                  account={account}
                  onRefresh={() => refresh(account)}
                  onSync={() => sync(true)}
                  onLogin={logout}
                />
                <ChangePassword onChanged={logout} />
                {account.role === "administrator" && <NewUser />}
              </section>
            )}
          </>
        )}
      </main>
      <nav className="bottom-nav">
        {(
          [
            ["home", "Início"],
            ["workout", "Treinar"],
            ["history", "Histórico"],
            ["catalog", "Fichas"],
            ["reports", "Relatórios"],
          ] as const
        ).map(([key, label]) => (
          <button
            className={tab === key ? "active" : ""}
            key={key}
            onClick={() => setTab(key)}
          >
            {label}
          </button>
        ))}
      </nav>
    </div>
  );
}
function Login({
  onLogin,
  notice,
}: {
  onLogin: (email: string, password: string) => Promise<void>;
  notice: string;
}) {
  const [email, setEmail] = useState(""),
    [password, setPassword] = useState(""),
    [error, setError] = useState(""),
    [forgot, setForgot] = useState(false);
  return (
    <main className="login-page">
      <div className="login-card">
        <div className="brand">
          <span className="brand-icon">◆</span> Treinos
        </div>
        <span className="eyebrow">CONSISTÊNCIA COMEÇA AQUI</span>
        <h1>{forgot ? "Recuperar senha" : "Bem-vindo de volta"}</h1>
        <p>
          {forgot
            ? "Informe seu e-mail para receber instruções quando o serviço estiver disponível."
            : "Entre para acessar suas fichas e registrar cada conquista."}
        </p>
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            setError("");
            try {
              if (forgot) {
                await api("/auth/forgot-password", {
                  method: "POST",
                  body: JSON.stringify({ email }),
                });
                setError("Se a conta existir, enviaremos instruções.");
              } else await onLogin(email, password);
            } catch (err) {
              setError(
                (err as Error).message === "EMAIL_NOT_CONFIGURED"
                  ? "Recuperação por e-mail indisponível no momento."
                  : (err as Error).message,
              );
            }
          }}
        >
          <label>
            E-mail
            <input
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
            />
          </label>
          {!forgot && (
            <label>
              Senha
              <input
                type="password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
              />
            </label>
          )}
          <button type="submit">
            {forgot ? "Solicitar recuperação" : "Entrar"}
          </button>
        </form>
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        {notice && <p>{notice}</p>}
        <button
          className="text"
          onClick={() => {
            setForgot(!forgot);
            setError("");
          }}
        >
          {forgot ? "Voltar ao login" : "Esqueci minha senha"}
        </button>
      </div>
    </main>
  );
}
function ResetPassword() {
  const [credentials] = useState(() => {
    const q = new URLSearchParams(window.location.search);
    const id = q.get("id") || "",
      token = q.get("token") || "";
    return { id, token };
  });
  useEffect(() => {
    window.history.replaceState({}, "", window.location.pathname);
  }, []);
  const [password, setPassword] = useState(""),
    [confirm, setConfirm] = useState(""),
    [message, setMessage] = useState("");
  return (
    <main className="login-page">
      <div className="login-card">
        <div className="brand">
          <span className="brand-icon">◆</span> Treinos
        </div>
        <h1>Definir nova senha</h1>
        <p>Escolha uma senha com pelo menos 12 caracteres.</p>
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            if (password !== confirm) {
              setMessage("As senhas não coincidem.");
              return;
            }
            try {
              await api("/auth/reset-password", {
                method: "POST",
                body: JSON.stringify({
                  id: credentials.id,
                  token: credentials.token,
                  newPassword: password,
                }),
              });
              setMessage("Senha alterada. Volte ao login.");
              setPassword("");
              setConfirm("");
            } catch {
              setMessage("Link inválido ou expirado. Solicite um novo link.");
            }
          }}
        >
          <label>
            Nova senha
            <input
              type="password"
              minLength={12}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </label>
          <label>
            Confirmar senha
            <input
              type="password"
              minLength={12}
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              required
            />
          </label>
          <button>Salvar nova senha</button>
        </form>
        {message && <p role="status">{message}</p>}
        <a href="/">Voltar ao login</a>
      </div>
    </main>
  );
}
function ChangePassword({ onChanged }: { onChanged: () => void }) {
  const [oldPassword, setOld] = useState(""),
    [newPassword, setNew] = useState(""),
    [message, setMessage] = useState("");
  return (
    <div className="password-box">
      <h3>Alterar senha</h3>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          try {
            await api("/auth/change-password", {
              method: "POST",
              body: JSON.stringify({ oldPassword, newPassword }),
            });
            setMessage("Senha alterada. Entre novamente.");
            onChanged();
          } catch (err) {
            setMessage((err as Error).message);
          }
        }}
      >
        <label>
          Senha atual
          <input
            type="password"
            value={oldPassword}
            onChange={(e) => setOld(e.target.value)}
            required
          />
        </label>
        <label>
          Nova senha (mínimo 12 caracteres)
          <input
            type="password"
            minLength={12}
            value={newPassword}
            onChange={(e) => setNew(e.target.value)}
            required
          />
        </label>
        <button>Alterar senha</button>
      </form>
      {message && <p>{message}</p>}
    </div>
  );
}
function NewUser() {
  const [name, setName] = useState(""),
    [email, setEmail] = useState(""),
    [initialPassword, setPassword] = useState(""),
    [message, setMessage] = useState("");
  return (
    <div className="password-box">
      <h3>Cadastrar usuário comum</h3>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          try {
            await api("/admin/users", {
              method: "POST",
              body: JSON.stringify({ name, email, initialPassword }),
            });
            setMessage(
              "Conta criada. Informe a senha inicial ao usuário por um meio seguro.",
            );
            setName("");
            setEmail("");
            setPassword("");
          } catch (err) {
            setMessage((err as Error).message);
          }
        }}
      >
        <label>
          Nome
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
          />
        </label>
        <label>
          E-mail
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
        </label>
        <label>
          Senha inicial
          <input
            type="password"
            minLength={12}
            value={initialPassword}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </label>
        <button>Cadastrar</button>
      </form>
      {message && <p>{message}</p>}
    </div>
  );
}
function Catalog({
  data,
  onReload,
}: {
  data: Bootstrap | null;
  onReload: () => Promise<void>;
}) {
  const [q, setQ] = useState(""),
    [editing, setEditing] = useState<Exercise | null>(null),
    [showForm, setShowForm] = useState(false),
    [message, setMessage] = useState("");
  const [name, setName] = useState(""),
    [muscleGroup, setMuscle] = useState(""),
    [equipment, setEquipment] = useState(""),
    [measurementType, setMeasure] = useState<"reps" | "duration">("reps"),
    [loadKind, setKind] = useState("external"),
    [loadBasis, setBasis] = useState("total"),
    [instructions, setInstructions] = useState("");
  const edit = (e?: Exercise) => {
    setEditing(e || null);
    setName(e?.name || "");
    setMuscle(e?.muscleGroup || "");
    setEquipment(e?.equipment || "");
    setMeasure(e?.measurementType || "reps");
    setKind(e?.loadKind || "external");
    setBasis(e?.loadBasis || "total");
    setInstructions(e?.instructions || "");
    setShowForm(true);
  };
  const submit = async (ev: React.FormEvent) => {
    ev.preventDefault();
    setMessage("");
    try {
      await api("/exercises" + (editing ? "/" + editing.id : ""), {
        method: editing ? "PUT" : "POST",
        body: JSON.stringify({
          name,
          muscleGroup,
          equipment,
          measurementType,
          loadKind,
          loadBasis,
          instructions,
          expectedVersion: editing?.rowVersion || 0,
        }),
      });
      setShowForm(false);
      onReload();
    } catch (err) {
      setMessage((err as Error).message);
    }
  };
  const remove = async (e: Exercise) => {
    if (!confirm(`Arquivar ${e.name}?`)) return;
    try {
      await api(`/exercises/${e.id}?expectedVersion=${e.rowVersion}`, {
        method: "DELETE",
      });
      onReload();
    } catch (err) {
      setMessage((err as Error).message);
    }
  };
  return (
    <section className="grid">
      <div className="card">
        <div className="section-title">
          <h2>Suas fichas</h2>
          <button className="secondary small" onClick={onReload}>
            Atualizar
          </button>
        </div>
        <PlansManager data={data} onReload={onReload} />
        {data?.plans.map((p) => (
          <div key={p.id}>
            <h3>{p.name}</h3>
            {p.templates.map((t) => (
              <details key={t.id}>
                <summary>
                  {t.name} · {t.items.length} exercícios
                </summary>
                {t.items.map((i) => (
                  <p key={i.id}>
                    {i.position}.{" "}
                    {data.exercises.find((e) => e.id === i.exerciseId)?.name} ·{" "}
                    {i.targetSets} séries ·{" "}
                    {i.repsMin
                      ? `${i.repsMin}–${i.repsMax} reps`
                      : `${i.durationSecondsMin}–${i.durationSecondsMax} s`}
                  </p>
                ))}
              </details>
            ))}
          </div>
        ))}
        {!data && <p>Prepare este dispositivo para consultar as fichas.</p>}
      </div>
      <div className="card">
        <div className="section-title">
          <h2>Catálogo</h2>
          <button className="secondary small" onClick={() => edit()}>
            Novo exercício
          </button>
        </div>
        {message && (
          <p role="alert" className="error">
            {message}
          </p>
        )}
        {showForm && (
          <form onSubmit={submit}>
            <h3>{editing ? "Editar exercício" : "Novo exercício"}</h3>
            <label>
              Nome
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                maxLength={160}
              />
            </label>
            <label>
              Grupo muscular
              <input
                value={muscleGroup}
                onChange={(e) => setMuscle(e.target.value)}
                required
                maxLength={80}
              />
            </label>
            <label>
              Equipamento
              <input
                value={equipment}
                onChange={(e) => setEquipment(e.target.value)}
              />
            </label>
            <label>
              Medição
              <select
                value={measurementType}
                onChange={(e) =>
                  setMeasure(e.target.value as "reps" | "duration")
                }
              >
                <option value="reps">Repetições</option>
                <option value="duration">Duração</option>
              </select>
            </label>
            <label>
              Tipo de carga
              <select
                value={loadKind}
                onChange={(e) => setKind(e.target.value)}
              >
                <option value="external">Externa</option>
                <option value="bodyweight">Peso corporal</option>
                <option value="assisted">Assistida</option>
              </select>
            </label>
            <label>
              Convenção
              <select
                value={loadBasis}
                onChange={(e) => setBasis(e.target.value)}
              >
                <option value="total">kg totais</option>
                <option value="per_hand">kg por halter</option>
                <option value="machine_display">kg da máquina</option>
                <option value="added_weight">kg adicionais</option>
              </select>
            </label>
            <label>
              Instruções
              <textarea
                value={instructions}
                onChange={(e) => setInstructions(e.target.value)}
              />
            </label>
            <div className="actions">
              <button>Salvar</button>
              <button
                type="button"
                className="secondary"
                onClick={() => setShowForm(false)}
              >
                Cancelar
              </button>
            </div>
          </form>
        )}
        <input
          placeholder="Buscar exercício"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
        {data?.exercises
          .filter((e) => e.name.toLowerCase().includes(q.toLowerCase()))
          .map((e) => (
            <div className="list-row" key={e.id}>
              <div>
                <strong>{e.name}</strong>
                <small>
                  {e.muscleGroup} · {e.equipment}
                </small>
              </div>
              <div className="actions">
                <button className="text small" onClick={() => edit(e)}>
                  Editar
                </button>
                <button className="danger small" onClick={() => remove(e)}>
                  Arquivar
                </button>
              </div>
            </div>
          ))}
      </div>
    </section>
  );
}
createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={new QueryClient()}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>,
);
