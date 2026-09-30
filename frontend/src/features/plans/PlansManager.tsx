import { useEffect, useRef, useState } from "react";
import type {
  Bootstrap,
  Plan,
  Template,
  TemplateItem,
} from "../../shared/types";
import { ownerApi } from "../../shared/api";

export function PlansManager({
  data,
  onReload,
  ownerId,
}: {
  ownerId?: string;
  data: Bootstrap | null;
  onReload: () => Promise<void>;
}) {
  const [planId, setPlanId] = useState(data?.plans[0]?.id || ""),
    [planEditor, setPlanEditor] = useState<"new" | "edit" | null>(null),
    [originalPlan, setOriginalPlan] = useState<Plan | null>(null),
    [name, setName] = useState(""),
    [description, setDescription] = useState(""),
    [sheetMode, setSheetMode] = useState<"new" | "edit" | "view" | null>(null),
    [originalSheet, setOriginalSheet] = useState<Template | null>(null),
    [title, setTitle] = useState(""),
    [code, setCode] = useState(""),
    [notes, setNotes] = useState(""),
    [items, setItems] = useState<TemplateItem[]>([]),
    [message, setMessage] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [online, setOnline] = useState(navigator.onLine);
  const editorHeading = useRef<HTMLHeadingElement>(null);
  const plan = data?.plans.find((p) => p.id === planId);
  useEffect(() => {
    if (data && !data.plans.some((p) => p.id === planId)) {
      setPlanId(data.plans[0]?.id || "");
      setSheetMode(null);
    }
  }, [data]);
  useEffect(() => {
    const updateNetwork = () => setOnline(navigator.onLine);
    window.addEventListener("online", updateNetwork);
    window.addEventListener("offline", updateNetwork);
    return () => {
      window.removeEventListener("online", updateNetwork);
      window.removeEventListener("offline", updateNetwork);
    };
  }, []);
  useEffect(() => {
    if (sheetMode || planEditor) {
      editorHeading.current?.focus({ preventScroll: true });
      editorHeading.current?.scrollIntoView({
        behavior: "smooth",
        block: "start",
      });
    }
  }, [sheetMode, originalSheet?.id, planEditor]);
  function selectPlan(id: string) {
    setPlanId(id);
    setSheetMode(null);
    setPlanEditor(null);
    setError("");
    setMessage("");
  }
  function openPlan(editing?: Plan) {
    setOriginalPlan(editing || null);
    setPlanEditor(editing ? "edit" : "new");
    setName(editing?.name || "");
    setDescription(editing?.description || "");
    setSheetMode(null);
    setError("");
  }
  function openSheet(mode: "new" | "edit" | "view", sheet?: Template) {
    setSheetMode(mode);
    setOriginalSheet(sheet || null);
    setPlanEditor(null);
    setError("");
    setTitle(sheet?.name || "");
    setCode(sheet?.code || "");
    setNotes(sheet?.notes || "");
    setItems(
      sheet?.items.map((i) => ({ ...i, alternatives: [...i.alternatives] })) ||
        [],
    );
  }
  async function savePlan(e: React.FormEvent) {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      const result = await ownerApi<{ id: string } | undefined>(
        ownerId,
        "/trainings" + (originalPlan ? "/" + originalPlan.id : ""),
        {
          method: originalPlan ? "PUT" : "POST",
          body: JSON.stringify({
            name,
            description,
            expectedVersion: originalPlan?.rowVersion || 0,
          }),
        },
      );
      await onReload();
      setPlanEditor(null);
      setPlanId(originalPlan?.id || result!.id);
      setMessage(
        originalPlan
          ? "Treino atualizado."
          : "Treino criado. Agora você pode adicionar suas fichas.",
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function saveTemplate(e: React.FormEvent) {
    e.preventDefault();
    if (busy || !plan) return;
    setBusy(true);
    setError("");
    try {
      await ownerApi(
        ownerId,
        originalSheet
          ? `/sheets/${originalSheet.id}?planId=${plan.id}`
          : `/trainings/${plan.id}/sheets`,
        {
          method: originalSheet ? "PUT" : "POST",
          body: JSON.stringify({
            code,
            name: title,
            position:
              originalSheet?.position ||
              Math.max(0, ...plan.templates.map((t) => t.position)) + 1,
            notes,
            expectedVersion: originalSheet?.rowVersion || 0,
            items: items.map((i, n) => ({
              ...i,
              position: n + 1,
              restScope:
                i.repetitionScope === "per_side"
                  ? "after_both_sides"
                  : "after_set",
            })),
          }),
        },
      );
      await onReload();
      setSheetMode(null);
      setMessage(originalSheet ? "Ficha atualizada." : "Ficha criada.");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function remove(
    kind: "trainings" | "sheets",
    id: string,
    version: number,
    label: string,
  ) {
    if (
      busy ||
      !confirm(
        `Excluir ${kind === "sheets" ? "a ficha" : "o treino"} “${label}”? ${kind === "trainings" ? "Suas fichas também deixarão de aparecer. " : ""}O histórico de execuções será preservado.`,
      )
    )
      return;
    setBusy(true);
    setError("");
    try {
      await ownerApi(ownerId, `/${kind}/${id}?expectedVersion=${version}`, {
        method: "DELETE",
      });
      await onReload();
      if (kind === "trainings") {
        setPlanEditor(null);
        setSheetMode(null);
      } else if (originalSheet?.id === id) setSheetMode(null);
      setMessage(
        kind === "sheets"
          ? "Ficha excluída. O histórico foi preservado."
          : "Treino excluído. O histórico foi preservado.",
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  function add() {
    const ex = data?.exercises[0];
    if (!ex) return;
    setItems([
      ...items,
      {
        id: crypto.randomUUID(),
        exerciseId: ex.id,
        position: items.length + 1,
        targetSets: 3,
        repsMin: ex.measurementType === "reps" ? 8 : undefined,
        repsMax: ex.measurementType === "reps" ? 12 : undefined,
        durationSecondsMin: ex.measurementType === "duration" ? 30 : undefined,
        durationSecondsMax: ex.measurementType === "duration" ? 45 : undefined,
        restSecondsMin: 60,
        restSecondsMax: 90,
        repetitionScope: "total",
        alternatives: [],
      },
    ]);
  }
  function update(index: number, patch: Partial<TemplateItem>) {
    setItems(items.map((i, n) => (n === index ? { ...i, ...patch } : i)));
  }
  function move(index: number, direction: number) {
    const other = index + direction;
    if (other < 0 || other >= items.length) return;
    const next = [...items];
    [next[index], next[other]] = [next[other], next[index]];
    setItems(next);
  }
  return (
    <section className="card sheets-manager" aria-label="Gerenciar fichas">
      <div className="section-title">
        <div>
          <h2>Treinos e fichas</h2>
          <p>Escolha um treino para consultar e gerenciar suas fichas.</p>
        </div>
        <button
          className="secondary"
          disabled={busy || !online}
          onClick={() => onReload().catch((e) => setError(e.message))}
        >
          Atualizar fichas
        </button>
      </div>
      {!online && (
        <p role="status">
          As fichas disponíveis neste dispositivo podem ser consultadas offline.
          Para criar, editar ou excluir, conecte-se à internet.
        </p>
      )}
      <div className="training-toolbar">
        <label>
          Treino
          <select
            aria-label="Treino"
            value={planId}
            disabled={busy}
            onChange={(e) => selectPlan(e.target.value)}
          >
            {!data?.plans.length && (
              <option value="">Nenhum treino cadastrado</option>
            )}
            {data?.plans.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </label>
        <div className="actions">
          <button
            className="secondary"
            disabled={busy || !online}
            onClick={() => openPlan()}
          >
            Novo treino
          </button>
          {plan && (
            <>
              <button
                className="text"
                disabled={busy || !online}
                onClick={() => openPlan(plan)}
              >
                Editar treino
              </button>
              <button
                className="danger"
                disabled={busy || !online}
                onClick={() =>
                  remove("trainings", plan.id, plan.rowVersion, plan.name)
                }
              >
                Excluir treino
              </button>
            </>
          )}
        </div>
      </div>
      {message && (
        <p className="notice" role="status">
          {message}
        </p>
      )}
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {planEditor && (
        <form className="editor" onSubmit={savePlan}>
          <fieldset disabled={busy || !online}>
            <h3 ref={editorHeading} tabIndex={-1}>
              {planEditor === "new" ? "Novo treino" : "Editar treino"}
            </h3>
            <label>
              Nome do treino
              <input
                required
                maxLength={160}
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </label>
            <label>
              Descrição
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
              />
            </label>
            <div className="actions">
              <button>
                {busy
                  ? "Salvando…"
                  : planEditor === "new"
                    ? "Criar treino"
                    : "Salvar treino"}
              </button>
              <button
                type="button"
                className="secondary"
                onClick={() => setPlanEditor(null)}
              >
                Cancelar
              </button>
            </div>
          </fieldset>
        </form>
      )}
      <div className="section-title sheets-heading">
        <div>
          <h3>Fichas{plan ? ` de ${plan.name}` : ""}</h3>
          <small>{plan?.templates.length || 0} ficha(s)</small>
        </div>
        <button
          disabled={!plan || busy || !online}
          onClick={() => openSheet("new")}
        >
          Nova ficha
        </button>
      </div>
      {!data ? (
        <p>
          Carregue seus treinos para consultar as fichas. Se estiver online, use
          Atualizar fichas.
        </p>
      ) : !plan ? (
        <p>Você ainda não tem treinos. Clique em Novo treino para começar.</p>
      ) : !plan.templates.length ? (
        <p>
          Nenhuma ficha neste treino. Clique em Nova ficha para adicionar
          exercícios e montar a primeira.
        </p>
      ) : (
        <div className="sheet-list">
          {[...plan.templates]
            .sort((a, b) => a.position - b.position)
            .map((sheet) => (
              <article
                className="sheet-card"
                key={sheet.id}
                aria-label={`Ficha ${sheet.name}`}
              >
                <div>
                  <span className="sheet-code">{sheet.code}</span>
                  <h4>{sheet.name}</h4>
                  <small>{sheet.items.length} exercícios</small>
                  {sheet.notes && (
                    <p className="exercise-instructions">{sheet.notes}</p>
                  )}
                </div>
                <div className="actions">
                  <button
                    className="secondary"
                    disabled={busy}
                    onClick={() => openSheet("view", sheet)}
                  >
                    Ver ficha
                  </button>
                  <button
                    className="secondary"
                    disabled={busy || !online}
                    onClick={() => openSheet("edit", sheet)}
                  >
                    Editar ficha
                  </button>
                  <button
                    className="danger"
                    disabled={busy || !online}
                    onClick={() =>
                      remove("sheets", sheet.id, sheet.rowVersion, sheet.name)
                    }
                  >
                    Excluir ficha
                  </button>
                </div>
              </article>
            ))}
        </div>
      )}
      {plan && sheetMode === "view" && originalSheet && (
        <section className="editor sheet-view" aria-label="Detalhes da ficha">
          <div className="section-title">
            <h3 ref={editorHeading} tabIndex={-1}>
              {originalSheet.name}
            </h3>
            <button className="text" onClick={() => setSheetMode(null)}>
              Fechar ficha
            </button>
          </div>
          <p>
            Treino: {plan.name} · Código: {originalSheet.code}
          </p>
          {originalSheet.notes && (
            <p className="exercise-instructions">{originalSheet.notes}</p>
          )}
          <ol>
            {originalSheet.items.map((item) => {
              const ex = data?.exercises.find((e) => e.id === item.exerciseId);
              return (
                <li key={item.id}>
                  <h4>
                    {ex?.name || "Exercício arquivado — substitua na edição"}
                  </h4>
                  <p>
                    {item.targetSets} séries ·{" "}
                    {ex?.measurementType === "duration" ||
                    item.durationSecondsMin
                      ? `${item.durationSecondsMin}–${item.durationSecondsMax} segundos`
                      : `${item.repsMin}–${item.repsMax} repetições`}
                    {item.repetitionScope === "per_side" ? " por lado" : ""} ·
                    Descanso: {item.restSecondsMin}–{item.restSecondsMax} s
                  </p>
                  {ex?.equipment && <p>Equipamento: {ex.equipment}</p>}
                  {ex?.instructions && (
                    <p className="exercise-instructions">{ex.instructions}</p>
                  )}
                  {item.notes && (
                    <p className="exercise-instructions">{item.notes}</p>
                  )}
                  {!!item.alternatives.length && (
                    <p>
                      Alternativas:{" "}
                      {item.alternatives
                        .map(
                          (id) =>
                            data?.exercises.find((e) => e.id === id)?.name ||
                            "Exercício arquivado",
                        )
                        .join(", ")}
                    </p>
                  )}
                </li>
              );
            })}
          </ol>
          <button
            className="secondary"
            disabled={busy || !online}
            onClick={() => openSheet("edit", originalSheet)}
          >
            Editar esta ficha
          </button>
        </section>
      )}
      {plan && (sheetMode === "new" || sheetMode === "edit") && (
        <form className="editor sheet-editor" onSubmit={saveTemplate}>
          <fieldset disabled={busy || !online}>
            <h3 ref={editorHeading} tabIndex={-1}>
              {sheetMode === "new"
                ? "Nova ficha"
                : `Editar ficha: ${originalSheet?.name}`}
            </h3>
            <p>
              Treino: <strong>{plan.name}</strong>
            </p>
            <div className="sheet-fields">
              <label>
                Nome da ficha
                <input
                  required
                  maxLength={120}
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                />
              </label>
              <label>
                Código
                <input
                  required
                  maxLength={20}
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </label>
            </div>
            <label>
              Orientações
              <textarea
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
              />
            </label>
            <h4>Exercícios da ficha</h4>
            {!data?.exercises.length && (
              <p>Cadastre exercícios no catálogo antes de montar a ficha.</p>
            )}
            {!items.length && !!data?.exercises.length && (
              <p>
                Clique em Adicionar exercício para montar a ficha. Você pode
                trocar, reordenar e remover os exercícios.
              </p>
            )}
            {items.map((item, n) => {
              const ex = data?.exercises.find((e) => e.id === item.exerciseId);
              const duration = ex?.measurementType === "duration";
              return (
                <div className="exercise" key={item.id}>
                  <div className="section-title">
                    <strong>Exercício {n + 1}</strong>
                    <div>
                      <button
                        type="button"
                        className="text"
                        aria-label={`Subir exercício ${n + 1}`}
                        disabled={n === 0}
                        onClick={() => move(n, -1)}
                      >
                        ↑
                      </button>
                      <button
                        type="button"
                        className="text"
                        aria-label={`Descer exercício ${n + 1}`}
                        disabled={n === items.length - 1}
                        onClick={() => move(n, 1)}
                      >
                        ↓
                      </button>
                    </div>
                  </div>
                  <label>
                    Exercício
                    <select
                      aria-label="Exercício"
                      value={item.exerciseId}
                      onChange={(e) => {
                        const selected = data?.exercises.find(
                          (x) => x.id === e.target.value,
                        );
                        update(n, {
                          exerciseId: e.target.value,
                          alternatives: [],
                          repsMin:
                            selected?.measurementType === "reps"
                              ? 8
                              : undefined,
                          repsMax:
                            selected?.measurementType === "reps"
                              ? 12
                              : undefined,
                          durationSecondsMin:
                            selected?.measurementType === "duration"
                              ? 30
                              : undefined,
                          durationSecondsMax:
                            selected?.measurementType === "duration"
                              ? 45
                              : undefined,
                        });
                      }}
                    >
                      {!ex && (
                        <option value={item.exerciseId}>
                          Exercício arquivado — substitua
                        </option>
                      )}
                      {data?.exercises.map((x) => (
                        <option key={x.id} value={x.id}>
                          {x.name}
                        </option>
                      ))}
                    </select>
                  </label>
                  <div className="compact-fields">
                    <label>
                      Séries
                      <input
                        type="number"
                        min={1}
                        max={100}
                        value={item.targetSets}
                        onChange={(e) =>
                          update(n, { targetSets: Number(e.target.value) })
                        }
                      />
                    </label>
                    <label>
                      {duration ? "Segundos mín." : "Reps mín."}
                      <input
                        type="number"
                        min={1}
                        value={
                          (duration ? item.durationSecondsMin : item.repsMin) ??
                          ""
                        }
                        onChange={(e) =>
                          update(
                            n,
                            duration
                              ? { durationSecondsMin: Number(e.target.value) }
                              : { repsMin: Number(e.target.value) },
                          )
                        }
                      />
                    </label>
                    <label>
                      {duration ? "Segundos máx." : "Reps máx."}
                      <input
                        type="number"
                        min={1}
                        value={
                          (duration ? item.durationSecondsMax : item.repsMax) ??
                          ""
                        }
                        onChange={(e) =>
                          update(
                            n,
                            duration
                              ? { durationSecondsMax: Number(e.target.value) }
                              : { repsMax: Number(e.target.value) },
                          )
                        }
                      />
                    </label>
                    <label>
                      Descanso mín. (s)
                      <input
                        type="number"
                        min={0}
                        value={item.restSecondsMin}
                        onChange={(e) =>
                          update(n, { restSecondsMin: Number(e.target.value) })
                        }
                      />
                    </label>
                    <label>
                      Descanso máx. (s)
                      <input
                        type="number"
                        min={0}
                        value={item.restSecondsMax}
                        onChange={(e) =>
                          update(n, { restSecondsMax: Number(e.target.value) })
                        }
                      />
                    </label>
                  </div>
                  <label>
                    Repetições
                    <select
                      value={item.repetitionScope}
                      onChange={(e) =>
                        update(n, {
                          repetitionScope: e.target
                            .value as TemplateItem["repetitionScope"],
                        })
                      }
                    >
                      <option value="total">Totais</option>
                      <option value="per_side">
                        Por lado — descanso após ambos
                      </option>
                    </select>
                  </label>
                  <label>
                    Alternativas (seleção múltipla)
                    <select
                      multiple
                      value={item.alternatives}
                      onChange={(e) =>
                        update(n, {
                          alternatives: Array.from(
                            e.target.selectedOptions,
                          ).map((o) => o.value),
                        })
                      }
                    >
                      {data?.exercises
                        .filter(
                          (x) =>
                            x.id !== item.exerciseId &&
                            x.measurementType === ex?.measurementType,
                        )
                        .map((x) => (
                          <option key={x.id} value={x.id}>
                            {x.name}
                          </option>
                        ))}
                    </select>
                  </label>
                  <label>
                    Observações
                    <textarea
                      value={item.notes || ""}
                      onChange={(e) => update(n, { notes: e.target.value })}
                    />
                  </label>
                  <button
                    type="button"
                    className="danger small"
                    onClick={() => setItems(items.filter((_, i) => i !== n))}
                  >
                    Remover item
                  </button>
                </div>
              );
            })}
            <div className="actions">
              <button
                type="button"
                className="secondary"
                onClick={add}
                disabled={!data?.exercises.length}
              >
                Adicionar exercício
              </button>
              <button disabled={!items.length}>
                {busy
                  ? "Salvando…"
                  : sheetMode === "new"
                    ? "Criar ficha"
                    : "Salvar alterações"}
              </button>
              <button
                type="button"
                className="secondary"
                onClick={() => setSheetMode(null)}
              >
                Cancelar
              </button>
            </div>
          </fieldset>
        </form>
      )}
    </section>
  );
}
