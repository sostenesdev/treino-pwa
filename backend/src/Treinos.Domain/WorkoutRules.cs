namespace Treinos.Domain;

public sealed class SetEntry
{
    public string Id { get; set; } = ""; public int SetNumber { get; set; } public string Side { get; set; } = "both"; public int? Repetitions { get; set; } public int? DurationSeconds { get; set; } public decimal? LoadKg { get; set; } public int? Rir { get; set; } public bool IsWarmup { get; set; } public bool IsCompleted { get; set; }
}
public record ExerciseEntry(string Id, string ExerciseId, int Position, string ExerciseNameSnapshot, string MeasurementTypeSnapshot, string LoadKindSnapshot, string LoadBasisSnapshot, string RepetitionScopeSnapshot, string CompletionStatus, List<SetEntry> Sets,
    string? EquipmentSnapshot=null, string? InstructionsSnapshot=null, int? TargetSetsSnapshot=null, int? RepsMinSnapshot=null, int? RepsMaxSnapshot=null, int? DurationMinSnapshot=null, int? DurationMaxSnapshot=null, int? RestMinSnapshot=null, int? RestMaxSnapshot=null, string? RestScopeSnapshot=null, string? PrescriptionNotesSnapshot=null, string? Notes=null);
public record SessionEntry(string Id, string? TemplateId, string WorkoutNameSnapshot, string PerformedOn, string TimeZoneSnapshot, string Status, string RecordingMode, string? Notes, List<ExerciseEntry> Exercises, long RowVersion = 0, bool Deleted = false,
    string? PlanNameSnapshot=null, bool IsIntroductoryWeek=false, string? PlanInstructionsSnapshot=null, DateTime? StartedAtUtc=null, DateTime? EndedAtUtc=null, DateTime? CompletedAtUtc=null);

public static class WorkoutRules
{
    public static void Validate(SessionEntry session, DateTimeOffset? now=null)
    {
        if (!Guid.TryParse(session.Id, out _) || !DateOnly.TryParseExact(session.PerformedOn, "yyyy-MM-dd", out _))
            throw new ArgumentException("ID ou data inválida.");
        TimeZoneInfo zone;
        try { zone=TimeZoneInfo.FindSystemTimeZoneById(session.TimeZoneSnapshot); }
        catch (Exception e) when(e is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new ArgumentException("Fuso horário inválido."); }
        if(DateOnly.Parse(session.PerformedOn)>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now??DateTimeOffset.UtcNow,zone).DateTime))throw new ArgumentException("O treino não pode ter data futura.");
        if(session.StartedAtUtc is not null && session.EndedAtUtc<session.StartedAtUtc)throw new ArgumentException("O fim deve ser posterior ao início.");
        if(session.TemplateId is not null&&!Guid.TryParse(session.TemplateId,out _))throw new ArgumentException("Ficha inválida.");
        if (string.IsNullOrWhiteSpace(session.WorkoutNameSnapshot) || session.WorkoutNameSnapshot.Length > 160)
            throw new ArgumentException("Nome do treino inválido.");
        if (session.Status is not ("in_progress" or "completed" or "cancelled") || session.RecordingMode is not ("quick" or "detailed"))
            throw new ArgumentException("Estado ou modo inválido.");
        if (session.Exercises is null || session.Exercises.Count is 0 or >100 || session.Exercises.Select(x => x.Position).Distinct().Count() != session.Exercises.Count || session.Exercises.Select(x=>x.Id).Distinct().Count()!=session.Exercises.Count)
            throw new ArgumentException("O treino precisa de exercícios com posições distintas.");
        if(session.Exercises.SelectMany(e=>e.Sets ?? []).Select(s=>s.Id).Distinct().Count()!=session.Exercises.Sum(e=>e.Sets?.Count ?? 0)) throw new ArgumentException("IDs de séries duplicados.");
        foreach (var exercise in session.Exercises)
        {
            if (!Guid.TryParse(exercise.Id, out _) || !Guid.TryParse(exercise.ExerciseId, out _) || exercise.Position < 1 || string.IsNullOrWhiteSpace(exercise.ExerciseNameSnapshot) || exercise.ExerciseNameSnapshot.Length>160 || exercise.Sets is null || exercise.Sets.Count>200 ||
                exercise.CompletionStatus is not ("planned" or "completed" or "skipped") ||
                exercise.MeasurementTypeSnapshot is not ("reps" or "duration") ||
                exercise.RepetitionScopeSnapshot is not ("total" or "per_side") ||
                exercise.LoadKindSnapshot is not ("external" or "bodyweight" or "assisted") ||
                exercise.LoadBasisSnapshot is not ("total" or "per_hand" or "machine_display" or "added_weight"))
                throw new ArgumentException("Exercício inválido.");
            if (exercise.Sets.Select(x => (x.SetNumber, x.Side)).Distinct().Count() != exercise.Sets.Count)
                throw new ArgumentException("Séries duplicadas.");
            foreach (var set in exercise.Sets)
            {
                if (!Guid.TryParse(set.Id, out _) || set.SetNumber < 1 ||
                    set.Side is not ("both" or "left" or "right") ||
                    (exercise.RepetitionScopeSnapshot == "per_side" ? set.Side == "both" : set.Side != "both") ||
                    set.LoadKg is < 0 or >99999.99m || set.Rir is < 0 or > 10 || set.Repetitions is <=0 or >65535 || set.DurationSeconds is <=0 ||
                    (set.Repetitions is not null && set.DurationSeconds is not null) ||
                    (set.IsCompleted && (exercise.MeasurementTypeSnapshot == "reps" ? set.Repetitions is null or <= 0 || set.DurationSeconds is not null : set.DurationSeconds is null or <= 0 || set.Repetitions is not null)))
                    throw new ArgumentException("Série inválida.");
            }
            if (session.Status == "completed" && (exercise.CompletionStatus == "planned" ||
                (session.RecordingMode == "detailed" && exercise.CompletionStatus == "completed" && !exercise.Sets.Any(x => x.IsCompleted && !x.IsWarmup))))
                throw new ArgumentException("Resolva os exercícios e registre uma série de trabalho.");
        }
        if (session.Status == "completed" && !session.Exercises.Any(x => x.CompletionStatus == "completed"))
            throw new ArgumentException("O treino concluído precisa de exercício realizado.");
    }
}

public record ExerciseProgressPoint(string PerformedOn, decimal? MaxLoadKg, int Repetitions, int DurationSeconds, decimal? VolumeKgReps, int SetsWithLoad, int EligibleSets, int MaxDurationSeconds);
public static class ProgressRules
{
    public static ExerciseProgressPoint Calculate(string date, IEnumerable<ExerciseEntry> exercises)
    {
        var all = exercises.Where(x=>x.CompletionStatus=="completed").SelectMany(x => x.Sets.Where(s => s.IsCompleted && !s.IsWarmup).Select(s => (Exercise:x, Set:s))).ToList();
        var measured = all.Where(x => x.Exercise.MeasurementTypeSnapshot == "reps" && x.Exercise.LoadKindSnapshot == "external").ToList();
        var known = measured.Where(x => x.Set.LoadKg is not null).ToList();
        var loads=all.Where(x=>x.Set.LoadKg is not null).ToList();
        return new(date, loads.Count == 0 ? null : loads.Max(x => x.Set.LoadKg), all.Sum(x => x.Set.Repetitions ?? 0), all.Sum(x => x.Set.DurationSeconds ?? 0), known.Count == 0 ? null : known.Sum(x => x.Set.LoadKg!.Value * (x.Set.Repetitions ?? 0)), known.Count, measured.Count, all.Count==0?0:all.Max(x=>x.Set.DurationSeconds??0));
    }
}
