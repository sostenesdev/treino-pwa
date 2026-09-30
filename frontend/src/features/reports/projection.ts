import type { Session } from "../../shared/types";
export type Point = {
  performedOn: string;
  maxLoadKg: number | null;
  repetitions: number;
  durationSeconds: number;
  maxDurationSeconds: number;
  volumeKgReps: number | null;
  setsWithLoad: number;
  eligibleSets: number;
};
export function localPoint(session: Session, id: string): Point {
  const exercises = session.exercises.filter(
    (e) => e.exerciseId === id && e.completionStatus === "completed",
  );
  const all = exercises.flatMap((e) =>
    e.sets
      .filter((s) => s.isCompleted && !s.isWarmup)
      .map((set) => ({ exercise: e, set })),
  );
  const eligible = all.filter(
    (x) =>
      x.exercise.measurementTypeSnapshot === "reps" &&
      x.exercise.loadKindSnapshot === "external",
  );
  const known = eligible.filter((x) => x.set.loadKg !== null);
  const loads = all.filter((x) => x.set.loadKg !== null);
  return {
    performedOn: session.performedOn,
    maxLoadKg: loads.length
      ? Math.max(...loads.map((x) => x.set.loadKg!))
      : null,
    repetitions: all.reduce((n, x) => n + (x.set.repetitions ?? 0), 0),
    durationSeconds: all.reduce((n, x) => n + (x.set.durationSeconds ?? 0), 0),
    maxDurationSeconds: Math.max(
      0,
      ...all.map((x) => x.set.durationSeconds ?? 0),
    ),
    volumeKgReps: known.length
      ? known.reduce((n, x) => n + x.set.loadKg! * (x.set.repetitions ?? 0), 0)
      : null,
    setsWithLoad: known.length,
    eligibleSets: eligible.length,
  };
}
