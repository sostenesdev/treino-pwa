import { expect, it } from 'vitest';
import { localPoint } from '../src/features/reports/projection';
import type { Session, SetEntry } from '../src/shared/types';
function point(loads: (number | null)[], sides: SetEntry['side'][] = loads.map(() => 'both'), measure = 'reps', kind = 'external') {
  return localPoint({ performedOn: '2026-09-29', exercises: [{ exerciseId: 'exercise', completionStatus: 'completed', measurementTypeSnapshot: measure, loadKindSnapshot: kind, loadBasisSnapshot: 'per_hand', sets: loads.map((loadKg, n) => ({ setNumber: n + 1, side: sides[n], loadKg, repetitions: measure === 'reps' ? 10 : null, durationSeconds: measure === 'duration' ? 30 : null, isWarmup: false, isCompleted: true })) }] } as Session, 'exercise');
}
it('mantém carga nula e cobertura parcial sem multiplicar halteres', () => {
  expect(point([20, null])).toMatchObject({ volumeKgReps: 200, maxLoadKg: 20, setsWithLoad: 1, eligibleSets: 2 });
  expect(point([null])).toMatchObject({ volumeKgReps: null, maxLoadKg: null });
  expect(point([0])).toMatchObject({ volumeKgReps: 0, maxLoadKg: 0 });
});
it('soma lados, mede duração e mantém assistência fora do volume', () => {
  expect(point([8, 8], ['left', 'right']).volumeKgReps).toBe(160);
  expect(point([null, null], undefined, 'duration')).toMatchObject({ volumeKgReps: null, maxDurationSeconds: 30, durationSeconds: 60 });
  expect(point([30], undefined, 'reps', 'assisted')).toMatchObject({ maxLoadKg: 30, volumeKgReps: null, eligibleSets: 0 });
});
