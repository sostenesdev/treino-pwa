import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  ResponsiveContainer,
  LineChart,
  Line,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
} from "recharts";
import type { Bootstrap, Session } from "../../shared/types";
import { ownerApi } from "../../shared/api";
import { today } from "../../shared/date";
import { localPoint, type Point } from "./projection";
export function Reports({
  accountId,
  ownerId,
  history,
  data,
}: {
  accountId: string;
  ownerId?: string;
  history: Session[];
  data: Bootstrap | null;
}) {
  const [from, setFrom] = useState(() => {
      const d = new Date();
      d.setDate(d.getDate() - 90);
      return d.toISOString().slice(0, 10);
    }),
    [to, setTo] = useState(today()),
    [exerciseId, setExercise] = useState(""),
    [source, setSource] = useState<"local" | "server">(
      ownerId ? "server" : "local",
    );
  const options = new Map<
    string,
    {
      name: string;
      measurementType: string;
      loadKind: string;
      loadBasis: string;
    }
  >();
  data?.exercises.forEach((e) => options.set(e.id, e));
  history.forEach((s) =>
    s.exercises.forEach((e) => {
      if (!options.has(e.exerciseId))
        options.set(e.exerciseId, {
          name: e.exerciseNameSnapshot,
          measurementType: e.measurementTypeSnapshot,
          loadKind: e.loadKindSnapshot,
          loadBasis: e.loadBasisSnapshot,
        });
    }),
  );
  const selected = options.get(exerciseId);
  const valid =
    !!from &&
    !!to &&
    from <= to &&
    (Date.parse(to) - Date.parse(from)) / 86400000 <= 365;
  const done = history.filter(
    (s) =>
      s.status === "completed" &&
      !s.deleted &&
      s.performedOn >= from &&
      s.performedOn <= to,
  );
  const months = new Map<string, number>();
  done.forEach((s) =>
    months.set(
      s.performedOn.slice(0, 7),
      (months.get(s.performedOn.slice(0, 7)) || 0) + 1,
    ),
  );
  const localFrequency = {
    sessions: done.length,
    distinctDays: new Set(done.map((s) => s.performedOn)).size,
    byMonth: [...months]
      .map(([month, sessions]) => ({ month, sessions }))
      .sort((a, b) => a.month.localeCompare(b.month)),
  };
  const frequency = useQuery({
    queryKey: [accountId, "frequency", from, to],
    queryFn: () =>
      ownerApi<typeof localFrequency>(
        ownerId,
        `/reports/frequency?from=${from}&to=${to}`,
      ),
    enabled: source === "server" && valid,
    retry: false,
  });
  const progress = useQuery({
    queryKey: [accountId, "progress", exerciseId, from, to],
    queryFn: () =>
      ownerApi<Point[]>(
        ownerId,
        `/reports/exercises/${exerciseId}?from=${from}&to=${to}`,
      ),
    enabled: source === "server" && valid && !!exerciseId,
    retry: false,
  });
  const f = source === "server" ? frequency.data : localFrequency;
  const points =
    source === "server"
      ? progress.data || []
      : done
          .filter((s) => s.exercises.some((e) => e.exerciseId === exerciseId))
          .map((s) => localPoint(s, exerciseId))
          .filter(
            (p) => p.eligibleSets || p.durationSeconds || p.maxLoadKg !== null,
          )
          .sort((a, b) => a.performedOn.localeCompare(b.performedOn));
  const basis =
    selected?.loadBasis === "per_hand"
      ? "kg por halter"
      : selected?.loadBasis === "machine_display"
        ? "kg da máquina"
        : selected?.loadBasis === "added_weight"
          ? "kg adicionais"
          : "kg totais";
  return (
    <section className="card">
      <div className="compact-fields">
        <label>
          De
          <input
            type="date"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
        </label>
        <label>
          Até
          <input
            type="date"
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />
        </label>
        <label>
          Fonte
          <select
            value={source}
            onChange={(e) => setSource(e.target.value as typeof source)}
          >
            {!ownerId && (
              <option value="local">Local, incluindo pendências</option>
            )}
            <option value="server">Servidor — requer conexão</option>
          </select>
        </label>
      </div>
      {!valid && (
        <p role="alert" className="error">
          Selecione um período válido de até 366 dias.
        </p>
      )}
      <p>
        {source === "local"
          ? `Visão local/parcial: histórico preparado de ${data?.historyDays || 90} dias e treinos ainda não enviados.`
          : "Visão dos treinos confirmados no servidor."}
      </p>
      {source === "server" && (frequency.error || progress.error) && (
        <p role="alert" className="error">
          Não foi possível atualizar o relatório online.
        </p>
      )}
      <div className="grid">
        <div className="metric">
          <span className="eyebrow">TREINOS CONCLUÍDOS</span>
          <strong>{f?.sessions ?? "—"}</strong>
        </div>
        <div className="metric">
          <span className="eyebrow">DIAS COM TREINO</span>
          <strong>{f?.distinctDays ?? "—"}</strong>
        </div>
      </div>
      <h3>Frequência por mês</h3>
      {f?.byMonth.length ? (
        <>
          <div
            role="img"
            aria-label="Gráfico de frequência mensal"
            style={{ height: 220 }}
          >
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={f.byMonth}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="month" />
                <YAxis allowDecimals={false} />
                <Tooltip />
                <Bar dataKey="sessions" name="Treinos" fill="#1e5b45" />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <table>
            <caption>Frequência mensal</caption>
            <thead>
              <tr>
                <th>Mês</th>
                <th>Treinos</th>
              </tr>
            </thead>
            <tbody>
              {f.byMonth.map((x) => (
                <tr key={x.month}>
                  <td>{x.month}</td>
                  <td>{x.sessions}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      ) : (
        <p>Nenhum treino concluído no período.</p>
      )}
      <label>
        Exercício
        <select
          value={exerciseId}
          onChange={(e) => setExercise(e.target.value)}
        >
          <option value="">Selecione para consultar progressão</option>
          {[...options].map(([id, e]) => (
            <option key={id} value={id}>
              {e.name}
            </option>
          ))}
        </select>
      </label>
      {selected && (
        <>
          <h3>
            {selected.name} ·{" "}
            {selected.measurementType === "duration" ? "segundos" : basis}
          </h3>
          {selected.loadKind === "assisted" && (
            <p>
              O valor representa assistência. Menos assistência pode indicar
              evolução; o gráfico não classifica recordes.
            </p>
          )}
          {points.length ? (
            <>
              <div
                role="img"
                aria-label="Gráfico de medidas por treino"
                style={{ height: 240 }}
              >
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={points}>
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis dataKey="performedOn" />
                    <YAxis />
                    <Tooltip />
                    <Line
                      dataKey={
                        selected.measurementType === "duration"
                          ? "maxDurationSeconds"
                          : "maxLoadKg"
                      }
                      name={
                        selected.measurementType === "duration"
                          ? "Máximo de segundos"
                          : basis
                      }
                      stroke="#1e5b45"
                      connectNulls={false}
                    />
                  </LineChart>
                </ResponsiveContainer>
              </div>
              <div className="table-scroll">
                <table>
                  <caption>Medidas registradas por treino</caption>
                  <thead>
                    <tr>
                      <th>Data</th>
                      <th>Carga máxima</th>
                      <th>Repetições</th>
                      <th>Segundos (máx./total)</th>
                      <th>Volume registrado</th>
                      <th>Cobertura</th>
                    </tr>
                  </thead>
                  <tbody>
                    {points.map((p, n) => (
                      <tr key={n}>
                        <td>{p.performedOn}</td>
                        <td>
                          {p.maxLoadKg === null
                            ? "Carga não informada"
                            : p.maxLoadKg}
                        </td>
                        <td>{p.repetitions}</td>
                        <td>
                          {p.maxDurationSeconds} / {p.durationSeconds}
                        </td>
                        <td>
                          {p.volumeKgReps === null
                            ? "Não informado"
                            : `${p.volumeKgReps} kg×reps${p.setsWithLoad < p.eligibleSets ? " (parcial)" : ""}`}
                        </td>
                        <td>
                          {p.setsWithLoad}/{p.eligibleSets}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <p>
                O volume usa a convenção registrada; pesos por halter não são
                multiplicados por dois.
              </p>
            </>
          ) : (
            <p>
              Nenhuma série de trabalho medida no período. O modo rápido conta
              na frequência.
            </p>
          )}
        </>
      )}
    </section>
  );
}
