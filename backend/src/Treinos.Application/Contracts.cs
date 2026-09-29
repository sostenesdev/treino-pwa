using Treinos.Domain;

namespace Treinos.Application;

public record Account(string Id, string DisplayName, string Email, string Role, bool MustChangePassword, string TimeZone);
public sealed class ExerciseDto
{
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string MuscleGroup { get; set; } = ""; public string? Equipment { get; set; } public string MeasurementType { get; set; } = ""; public string LoadKind { get; set; } = ""; public string LoadBasis { get; set; } = ""; public string? Instructions { get; set; } public long RowVersion { get; set; }
}
public record ExerciseInput(string Name, string MuscleGroup, string? Equipment, string MeasurementType, string LoadKind, string LoadBasis, string? Instructions, long ExpectedVersion = 0);
public record PlanDto(string Id, string Name, string? Description, List<TemplateDto> Templates, long RowVersion);
public record PlanInput(string Name, string? Description, long ExpectedVersion = 0);
public record TemplateInput(string Code, string Name, int Position, string? Notes, List<TemplateItemInput> Items, long ExpectedVersion = 0);
public record TemplateItemInput(string ExerciseId, int Position, int TargetSets, int? RepsMin, int? RepsMax, int? DurationSecondsMin, int? DurationSecondsMax, int RestSecondsMin, int RestSecondsMax, string RepetitionScope, List<string> Alternatives);
public record TemplateDto(string Id, string Code, string Name, int Position, string? Notes, List<TemplateItemDto> Items, long RowVersion);
public record TemplateItemDto(string Id, string ExerciseId, int Position, int TargetSets, int? RepsMin, int? RepsMax, int? DurationSecondsMin, int? DurationSecondsMax, int RestSecondsMin, int RestSecondsMax, string RepetitionScope, List<string> Alternatives);
public record SyncOperation(string OperationId, string DeviceId, int SchemaVersion, string EntityId, string Kind, long BaseVersion, SessionEntry? Snapshot);
public record SyncAck(string OperationId, string EntityId, long Version, long Revision);
public record SyncBootstrap(long Revision, List<ExerciseDto> Exercises, List<PlanDto> Plans, List<SessionEntry> Sessions, int HistoryDays);
public sealed class SyncChange
{
    public long Revision { get; set; } public string EntityType { get; set; } = ""; public string? EntityId { get; set; } public string ChangeKind { get; set; } = ""; public string? PayloadJson { get; set; }
}
public record SyncPage(long NextCursor, bool HasMore, long ObservedRevision, List<SyncChange> Changes);

public interface ICurrentUser { string Id { get; } }
public interface ITrainingService
{
    Task<List<ExerciseDto>> Exercises(CancellationToken ct);
    Task<ExerciseDto?> Exercise(string id, CancellationToken ct);
    Task<ExerciseDto> SaveExercise(string? id, ExerciseInput input, CancellationToken ct);
    Task<bool> DeleteExercise(string id, long expectedVersion, CancellationToken ct);
    Task<List<PlanDto>> Plans(CancellationToken ct);
    Task<string> SavePlan(string? id, PlanInput input, CancellationToken ct);
    Task<string> SaveTemplate(string? id, string planId, TemplateInput input, CancellationToken ct);
    Task<List<SessionEntry>> Sessions(CancellationToken ct);
    Task<SyncBootstrap> Bootstrap(CancellationToken ct);
    Task<SyncPage> Changes(long after, int limit, CancellationToken ct);
    Task<SyncAck> Apply(SyncOperation operation, CancellationToken ct);
    Task<object> Frequency(string from, string to, CancellationToken ct);
    Task<List<ExerciseProgressPoint>> ExerciseProgress(string exerciseId, string from, string to, CancellationToken ct);
}
