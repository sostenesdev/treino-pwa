import { useState } from "react";
import type {
  Bootstrap,
  Plan,
  Template,
  TemplateItem,
} from "../../shared/types";
import { api } from "../../shared/api";
export function PlansManager({
  data,
  onReload,
}: {
  data: Bootstrap | null;
  onReload: () => Promise<void>;
}) {
  const [planId, setPlanId] = useState(""),
    [name, setName] = useState(""),
    [description, setDescription] = useState(""),
    [templateId, setTemplateId] = useState(""),
    [title, setTitle] = useState(""),
    [code, setCode] = useState(""),
    [notes, setNotes] = useState(""),
    [items, setItems] = useState<TemplateItem[]>([]),
    [message, setMessage] = useState("");
  const plan = data?.plans.find((p) => p.id === planId),
    template = plan?.templates.find((t) => t.id === templateId);
  function selectPlan(id: string) {
    setPlanId(id);
    const p = data?.plans.find((p) => p.id === id);
    setName(p?.name || "");
    setDescription(p?.description || "");
    selectTemplate("");
  }
  function selectTemplate(id: string) {
    setTemplateId(id);
    const t = plan?.templates.find((t) => t.id === id);
    setTitle(t?.name || "");
    setCode(t?.code || "");
    setNotes(t?.notes || "");
    setItems(
      t?.items.map((i) => ({ ...i, alternatives: [...i.alternatives] })) || [],
    );
  }
  async function savePlan(e: React.FormEvent) {
    e.preventDefault();
    try {
      await api("/plans" + (planId ? "/" + planId : ""), {
        method: planId ? "PUT" : "POST",
        body: JSON.stringify({
          name,
          description,
          expectedVersion: plan?.rowVersion || 0,
        }),
      });
      await onReload();
      selectPlan("");
      setMessage("Plano salvo.");
    } catch (e) {
      setMessage((e as Error).message);
    }
  }
  async function saveTemplate(e: React.FormEvent) {
    e.preventDefault();
    try {
      await api(
        templateId
          ? `/templates/${templateId}?planId=${planId}`
          : `/plans/${planId}/templates`,
        {
          method: templateId ? "PUT" : "POST",
          body: JSON.stringify({
            code,
            name: title,
            position: template?.position || (plan?.templates.length || 0) + 1,
            notes,
            expectedVersion: template?.rowVersion || 0,
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
      selectTemplate("");
      setMessage("Ficha salva.");
    } catch (e) {
      setMessage((e as Error).message);
    }
  }
  async function archive(
    kind: "plans" | "templates",
    id: string,
    version: number,
  ) {
    if (
      !confirm(
        "Arquivar esta definição? O histórico dos treinos será preservado.",
      )
    )
      return;
    try {
      await api(`/${kind}/${id}?expectedVersion=${version}`, {
        method: "DELETE",
      });
      await onReload();
      selectPlan("");
      setMessage("Definição arquivada.");
    } catch (e) {
      setMessage((e as Error).message);
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
    <details className="editor">
      <summary>Gerenciar planos e fichas</summary>
      {message && <p role="status">{message}</p>}
      <label>
        Plano
        <select value={planId} onChange={(e) => selectPlan(e.target.value)}>
          <option value="">Novo plano</option>
          {data?.plans.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
      </label>
      <form onSubmit={savePlan}>
        <label>
          Nome do plano
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            maxLength={160}
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
          <button className="secondary">
            {planId ? "Salvar plano" : "Criar plano"}
          </button>
          {plan && (
            <button
              type="button"
              className="danger"
              onClick={() => archive("plans", plan.id, plan.rowVersion)}
            >
              Arquivar plano
            </button>
          )}
        </div>
      </form>
      {plan && (
        <form onSubmit={saveTemplate}>
          <h3>Ficha do plano</h3>
          <label>
            Ficha
            <select
              value={templateId}
              onChange={(e) => selectTemplate(e.target.value)}
            >
              <option value="">Nova ficha</option>
              {plan.templates.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Nome da ficha
            <input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              required
              maxLength={120}
            />
          </label>
          <label>
            Código
            <input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              required
              maxLength={20}
            />
          </label>
          <label>
            Orientações
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </label>
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
                    value={item.exerciseId}
                    onChange={(e) => {
                      const selected = data?.exercises.find(
                        (x) => x.id === e.target.value,
                      );
                      update(n, {
                        exerciseId: e.target.value,
                        alternatives: [],
                        repsMin:
                          selected?.measurementType === "reps" ? 8 : undefined,
                        repsMax:
                          selected?.measurementType === "reps" ? 12 : undefined,
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
                        alternatives: Array.from(e.target.selectedOptions).map(
                          (o) => o.value,
                        ),
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
            <button disabled={!items.length}>Salvar ficha</button>
            {template && (
              <button
                type="button"
                className="danger"
                onClick={() =>
                  archive("templates", template.id, template.rowVersion)
                }
              >
                Arquivar ficha
              </button>
            )}
          </div>
        </form>
      )}
    </details>
  );
}
