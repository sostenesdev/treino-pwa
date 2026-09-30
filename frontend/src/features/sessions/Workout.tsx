import { useState } from "react";
import type {
  Bootstrap,
  ExerciseEntry,
  Session,
  SetEntry,
} from "../../shared/types";
import { numberInput, today } from "../../shared/date";
type Props = {
  data: Bootstrap | null;
  draft: Session | null;
  history: Session[];
  timeZone: string;
  onDraft: (s: Session | null) => void;
  onSave: (s: Session) => Promise<boolean>;
};
export function Workout({
  data,
  draft,
  history,
  timeZone,
  onDraft,
  onSave,
}: Props) {
  const [templateId, setTemplate] = useState(""),
    [date, setDate] = useState(today(timeZone)),
    [mode, setMode] = useState<"quick" | "detailed">("quick"),
    [intro, setIntro] = useState(false),
    [choices, setChoices] = useState<Record<string, string>>({}),
    [custom, setCustom] = useState<string[]>([]),
    [extra, setExtra] = useState(""),
    [error, setError] = useState("");
  const templates = data?.plans.flatMap((p) => p.templates) || [];
  const selected = templates.find((t) => t.id === templateId);
  function entry(
    exerciseId: string,
    position: number,
    item?: typeof selected extends undefined
      ? never
      : NonNullable<typeof selected>["items"][number],
  ): ExerciseEntry {
    const ex = data?.exercises.find((e) => e.id === exerciseId);
    if (!ex)
      throw Error(
        "Este exercício foi arquivado. Substitua ou remova o item da ficha.",
      );
    return {
      id: crypto.randomUUID(),
      exerciseId: ex.id,
      position,
      exerciseNameSnapshot: ex.name,
      equipmentSnapshot: ex.equipment,
      instructionsSnapshot: ex.instructions,
      measurementTypeSnapshot: ex.measurementType,
      loadKindSnapshot: ex.loadKind,
      loadBasisSnapshot: ex.loadBasis,
      repetitionScopeSnapshot: item?.repetitionScope || "total",
      completionStatus: "planned",
      sets: [],
      targetSetsSnapshot:
        intro && item?.targetSets === 3 ? 2 : item?.targetSets,
      repsMinSnapshot: item?.repsMin,
      repsMaxSnapshot: item?.repsMax,
      durationMinSnapshot: item?.durationSecondsMin,
      durationMaxSnapshot: item?.durationSecondsMax,
      restMinSnapshot: item?.restSecondsMin,
      restMaxSnapshot: item?.restSecondsMax,
      restScopeSnapshot: item?.restScope,
      prescriptionNotesSnapshot: item?.notes,
    };
  }
  async function start() {
    try {
      if (date > today(timeZone))
        throw Error("A data do treino não pode ser futura.");
      const plan = data?.plans.find((p) =>
        p.templates.some((t) => t.id === templateId),
      );
      const exercises = selected
        ? selected.items.map((i) =>
            entry(choices[i.id] || i.exerciseId, i.position, i),
          )
        : custom.map((id, n) => entry(id, n + 1));
      if (!exercises.length) throw Error("Adicione ao menos um exercício.");
      const s: Session = {
        id: crypto.randomUUID(),
        templateId: selected?.id || null,
        workoutNameSnapshot: selected?.name || "Treino avulso",
        performedOn: date,
        timeZoneSnapshot: timeZone,
        status: "in_progress",
        recordingMode: mode,
        notes: null,
        exercises,
        rowVersion: 0,
        isIntroductoryWeek: intro,
        planNameSnapshot: plan?.name,
        planInstructionsSnapshot: plan?.instructions,
      };
      onDraft(s);
      await onSave(s);
    } catch (e) {
      setError((e as Error).message);
    }
  }
  async function update(s: Session) {
    onDraft(s);
    return onSave(s);
  }
  async function updateExercise(ex: ExerciseEntry) {
    if (draft)
      await update({
        ...draft,
        exercises: draft.exercises.map((e) => (e.id === ex.id ? ex : e)),
      });
  }
  const catalogSelector = (
    <select
      aria-label="Adicionar exercício"
      value={extra}
      onChange={(e) => setExtra(e.target.value)}
    >
      <option value="">Selecione exercício</option>
      {data?.exercises.map((e) => (
        <option key={e.id} value={e.id}>
          {e.name}
        </option>
      ))}
    </select>
  );
  if (!draft)
    return (
      <section className="card">
        <h2>Novo registro</h2>
        <label>
          Ficha
          <select
            value={templateId}
            onChange={(e) => {
              setTemplate(e.target.value);
              setChoices({});
            }}
          >
            <option value="">Treino avulso</option>
            {templates.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Data do treino
          <input
            type="date"
            value={date}
            max={today(timeZone)}
            onChange={(e) => setDate(e.target.value)}
          />
        </label>
        <label>
          Modo
          <select
            value={mode}
            onChange={(e) => setMode(e.target.value as typeof mode)}
          >
            <option value="quick">Rápido — marcar exercícios</option>
            <option value="detailed">Detalhado — registrar séries</option>
          </select>
        </label>
        {selected && (
          <>
            <label className="check">
              <input
                type="checkbox"
                checked={intro}
                onChange={(e) => setIntro(e.target.checked)}
              />
              Semana de adaptação — 2 séries onde há 3
            </label>
            {selected.items
              .filter((i) => i.alternatives.length)
              .map((i) => (
                <label key={i.id}>
                  Escolha a variação
                  <select
                    value={choices[i.id] || i.exerciseId}
                    onChange={(e) =>
                      setChoices({ ...choices, [i.id]: e.target.value })
                    }
                  >
                    {[i.exerciseId, ...i.alternatives].map((id) => (
                      <option key={id} value={id}>
                        {data?.exercises.find((e) => e.id === id)?.name ||
                          "Exercício arquivado"}
                      </option>
                    ))}
                  </select>
                </label>
              ))}
          </>
        )}
        {!selected && (
          <>
            <h3>Exercícios do treino avulso</h3>
            {custom.map((id, n) => (
              <div className="list-row" key={n}>
                {data?.exercises.find((e) => e.id === id)?.name}
                <button
                  className="text"
                  onClick={() => setCustom(custom.filter((_, i) => i !== n))}
                >
                  Remover
                </button>
              </div>
            ))}
            {catalogSelector}
            <button
              className="secondary"
              disabled={!extra}
              onClick={() => {
                setCustom([...custom, extra]);
                setExtra("");
              }}
            >
              Adicionar exercício
            </button>
          </>
        )}
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        <div className="actions">
          <button onClick={start} disabled={!data}>
            Iniciar treino
          </button>
        </div>
        {history
          .filter((s) => s.status === "in_progress")
          .map((s) => (
            <div className="list-row" key={s.id}>
              <span>
                {s.workoutNameSnapshot} · {s.performedOn}
              </span>
              <button className="secondary" onClick={() => onDraft(s)}>
                Retomar
              </button>
            </div>
          ))}
      </section>
    );
  async function complete() {
    if (!draft) return;
    const s = { ...draft, status: "completed" as const };
    if (s.exercises.some((e) => e.completionStatus === "planned")) {
      setError("Marque todos os exercícios como realizados ou pulados.");
      return;
    }
    if (!s.exercises.some((e) => e.completionStatus === "completed")) {
      setError("Marque pelo menos um exercício realizado.");
      return;
    }
    if (
      s.recordingMode === "detailed" &&
      s.exercises.some(
        (e) =>
          e.completionStatus === "completed" &&
          !e.sets.some((x) => x.isCompleted && !x.isWarmup),
      )
    ) {
      setError(
        "Registre uma série de trabalho concluída em cada exercício realizado.",
      );
      return;
    }
    if (await onSave(s)) onDraft(null);
  }
  return (
    <section className="card">
      <div className="section-title">
        <h2>{draft.workoutNameSnapshot}</h2>
        <button className="text" onClick={() => onDraft(null)}>
          Fechar
        </button>
      </div>
      {draft.planInstructionsSnapshot && (
        <details>
          <summary>Orientações do plano</summary>
          <p>{draft.planInstructionsSnapshot}</p>
        </details>
      )}
      <label>
        Data realizada
        <input
          type="date"
          value={draft.performedOn}
          max={today(draft.timeZoneSnapshot)}
          onChange={(e) => {
            if (
              e.target.value &&
              e.target.value <= today(draft.timeZoneSnapshot)
            )
              update({ ...draft, performedOn: e.target.value });
          }}
        />
      </label>
      <label>
        Modo de registro
        <select
          value={draft.recordingMode}
          onChange={(e) =>
            update({
              ...draft,
              recordingMode: e.target.value as Session["recordingMode"],
            })
          }
        >
          <option value="quick">Rápido</option>
          <option value="detailed">Detalhado</option>
        </select>
      </label>
      <button
        className="secondary"
        onClick={() =>
          update({
            ...draft,
            exercises: draft.exercises.map((e) => ({
              ...e,
              completionStatus: "completed",
            })),
          })
        }
      >
        Marcar todos como realizados
      </button>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {draft.exercises.map((ex) => {
        const previous = history
          .filter((s) => s.id !== draft.id && s.status === "completed")
          .flatMap((s) =>
            s.exercises
              .filter((e) => e.exerciseId === ex.exerciseId)
              .flatMap((e) =>
                e.sets.filter(
                  (x) => x.isCompleted && !x.isWarmup && x.loadKg !== null,
                ),
              ),
          )[0];
        return (
          <div className="exercise" key={ex.id}>
            <div className="section-title">
              <div>
                <strong>
                  {ex.position}. {ex.exerciseNameSnapshot}
                </strong>
                <small>
                  {ex.targetSetsSnapshot || "—"} séries ·{" "}
                  {ex.measurementTypeSnapshot === "reps"
                    ? `${ex.repsMinSnapshot ?? "—"}–${ex.repsMaxSnapshot ?? "—"} reps`
                    : `${ex.durationMinSnapshot ?? "—"}–${ex.durationMaxSnapshot ?? "—"} s`}
                  {ex.repetitionScopeSnapshot === "per_side" ? " por lado" : ""}{" "}
                  · descanso {ex.restMinSnapshot ?? "—"}–
                  {ex.restMaxSnapshot ?? "—"} s
                </small>
                {previous && (
                  <small>
                    Última carga: {previous.loadKg} kg. Preencha a carga de hoje
                    se quiser registrá-la.
                  </small>
                )}
              </div>
              <select
                aria-label={`Estado de ${ex.exerciseNameSnapshot}`}
                value={ex.completionStatus}
                onChange={(e) =>
                  updateExercise({
                    ...ex,
                    completionStatus: e.target
                      .value as ExerciseEntry["completionStatus"],
                  })
                }
              >
                <option value="planned">Planejado</option>
                <option value="completed">Realizado</option>
                <option value="skipped">Pulado</option>
              </select>
            </div>
            {ex.instructionsSnapshot && (
              <details>
                <summary>Como registrar</summary>
                <p>{ex.instructionsSnapshot}</p>
              </details>
            )}
            {draft.recordingMode === "detailed" && (
              <>
                <div className="sets">
                  {ex.sets.map((set) => (
                    <SetEditor
                      key={set.id}
                      set={set}
                      exercise={ex}
                      onSave={async (value) => {
                        const updated = {
                          ...ex,
                          sets: ex.sets.map((s) =>
                            s.id === value.id ? value : s,
                          ),
                          completionStatus: value.isCompleted
                            ? ("completed" as const)
                            : ex.completionStatus,
                        };
                        await updateExercise(updated);
                      }}
                      onRemove={() =>
                        updateExercise({
                          ...ex,
                          sets: ex.sets.filter((s) => s.id !== set.id),
                        })
                      }
                    />
                  ))}
                </div>
                <button
                  className="secondary small"
                  onClick={() => {
                    const number =
                      Math.max(0, ...ex.sets.map((s) => s.setNumber)) + 1;
                    const sides: SetEntry["side"][] =
                      ex.repetitionScopeSnapshot === "per_side"
                        ? ["left", "right"]
                        : ["both"];
                    updateExercise({
                      ...ex,
                      sets: [
                        ...ex.sets,
                        ...sides.map((side) => ({
                          id: crypto.randomUUID(),
                          setNumber: number,
                          side,
                          repetitions: null,
                          durationSeconds: null,
                          loadKg: null,
                          rir: null,
                          isWarmup: false,
                          isCompleted: false,
                        })),
                      ],
                    });
                  }}
                >
                  Adicionar série
                  {ex.repetitionScopeSnapshot === "per_side"
                    ? " para cada lado"
                    : ""}
                </button>
              </>
            )}
            <button
              className="text small"
              onClick={() => {
                if (confirm("Remover este exercício do registro?"))
                  update({
                    ...draft,
                    exercises: draft.exercises
                      .filter((e) => e.id !== ex.id)
                      .map((e, n) => ({ ...e, position: n + 1 })),
                  });
              }}
            >
              Remover exercício
            </button>
          </div>
        );
      })}
      {catalogSelector}
      <button
        className="secondary"
        disabled={!extra}
        onClick={() => {
          try {
            update({
              ...draft,
              exercises: [
                ...draft.exercises,
                entry(extra, draft.exercises.length + 1),
              ],
            });
            setExtra("");
          } catch (e) {
            setError((e as Error).message);
          }
        }}
      >
        Adicionar exercício ao treino
      </button>
      <label>
        Observações
        <textarea
          value={draft.notes || ""}
          onChange={(e) => onDraft({ ...draft, notes: e.target.value })}
          onBlur={() => onSave(draft)}
        />
      </label>
      <div className="actions">
        <button onClick={complete}>Concluir treino</button>
        <button className="secondary" onClick={() => onSave(draft)}>
          Salvar rascunho
        </button>
      </div>
    </section>
  );
}
function SetEditor({
  set,
  exercise,
  onSave,
  onRemove,
}: {
  set: SetEntry;
  exercise: ExerciseEntry;
  onSave: (s: SetEntry) => Promise<void>;
  onRemove: () => void;
}) {
  const [measure, setMeasure] = useState(
      String(set.repetitions ?? set.durationSeconds ?? ""),
    ),
    [load, setLoad] = useState(String(set.loadKg ?? "")),
    [rir, setRir] = useState(String(set.rir ?? "")),
    [warmup, setWarmup] = useState(set.isWarmup),
    [error, setError] = useState("");
  const basis =
    exercise.loadBasisSnapshot === "per_hand"
      ? "kg por halter"
      : exercise.loadBasisSnapshot === "machine_display"
        ? "kg da máquina"
        : exercise.loadBasisSnapshot === "added_weight"
          ? "kg adicionais"
          : "kg totais";
  async function save(completed = set.isCompleted) {
    try {
      const quantity = numberInput(measure),
        weight = numberInput(load),
        reserve = numberInput(rir);
      if (quantity !== null && (!Number.isInteger(quantity) || quantity <= 0))
        throw Error("Informe uma medida inteira positiva.");
      if (completed && quantity === null)
        throw Error(
          "Informe repetições ou segundos antes de concluir a série.",
        );
      if (weight !== null && weight < 0)
        throw Error("A carga não pode ser negativa.");
      if (
        reserve !== null &&
        (!Number.isInteger(reserve) || reserve < 0 || reserve > 10)
      )
        throw Error("RIR deve estar entre 0 e 10.");
      await onSave({
        ...set,
        repetitions:
          exercise.measurementTypeSnapshot === "reps" ? quantity : null,
        durationSeconds:
          exercise.measurementTypeSnapshot === "duration" ? quantity : null,
        loadKg: weight,
        rir: reserve,
        isWarmup: warmup,
        isCompleted: completed,
      });
      setError("");
    } catch (e) {
      setError((e as Error).message);
    }
  }
  return (
    <div className="set-editor">
      <strong>
        Série {set.setNumber}
        {set.side !== "both"
          ? ` · ${set.side === "left" ? "esquerda" : "direita"}`
          : ""}
        {set.isCompleted ? " · concluída" : ""}
      </strong>
      <div className="compact-fields">
        <label>
          {exercise.measurementTypeSnapshot === "reps"
            ? "Repetições"
            : "Segundos"}
          <input
            inputMode="numeric"
            value={measure}
            onChange={(e) => setMeasure(e.target.value)}
            onBlur={() => save()}
          />
        </label>
        <label>
          Carga opcional · {basis}
          <input
            inputMode="decimal"
            value={load}
            placeholder="Não informada"
            onChange={(e) => setLoad(e.target.value)}
            onBlur={() => save()}
          />
        </label>
        <label>
          RIR opcional
          <input
            inputMode="numeric"
            value={rir}
            onChange={(e) => setRir(e.target.value)}
            onBlur={() => save()}
          />
        </label>
      </div>
      <label className="check">
        <input
          type="checkbox"
          checked={warmup}
          onChange={(e) => setWarmup(e.target.checked)}
        />
        Aquecimento
      </label>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <div className="actions">
        <button className="secondary small" onClick={() => save(true)}>
          Concluir série
        </button>
        <button className="text small" onClick={onRemove}>
          Excluir série
        </button>
      </div>
    </div>
  );
}
