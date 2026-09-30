using Treinos.Domain;

namespace Treinos.Application;

public record Account(string Id, string DisplayName, string Email, string Role, bool MustChangePassword, string TimeZone);
public record AuthenticationResult(Account Account, System.Security.Claims.ClaimsPrincipal Principal);
public record UserDto(string Id, string DisplayName, string Email, string Role, bool MustChangePassword, string TimeZone, long RowVersion);
public record UserInput(string Name, string Email, string Role = "common", string TimeZone = "America/Sao_Paulo", string? InitialPassword = null, long ExpectedVersion = 0);
public interface IAccountService
{
    Task<Account?> GetCurrent(CancellationToken ct = default);
    Task<Account?> ValidateSession(string id, string stamp, string role, CancellationToken ct = default);
    Task<AuthenticationResult?> Login(string email, string password, CancellationToken ct = default);
    Task<Account> CreateCommonUser(string name, string email, string initialPassword, CancellationToken ct = default);
    Task<List<UserDto>> Users(CancellationToken ct = default);
    Task<UserDto?> User(string id, CancellationToken ct = default);
    Task<UserDto> CreateUser(UserInput input, CancellationToken ct = default);
    Task<UserDto> UpdateUser(string id, UserInput input, CancellationToken ct = default);
    Task DeleteUser(string id, long expectedVersion, CancellationToken ct = default);
    Task<bool> ChangePassword(string oldPassword, string newPassword, CancellationToken ct = default);
}
public interface IPasswordRecovery
{
    bool Enabled { get; }
    Task Request(string email);
    Task<bool> Reset(string id, string token, string newPassword);
}
public sealed class ExerciseDto
{
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string MuscleGroup { get; set; } = ""; public string? Equipment { get; set; } public string MeasurementType { get; set; } = ""; public string LoadKind { get; set; } = ""; public string LoadBasis { get; set; } = ""; public string? Instructions { get; set; } public long RowVersion { get; set; }
}
public record ExerciseInput(string Name, string? MuscleGroup = null, string? Equipment = null, string? MeasurementType = null, string? LoadKind = null, string? LoadBasis = null, string? Instructions = null, long ExpectedVersion = 0);
public record PlanDto(string Id, string Name, string? Description, List<TemplateDto> Templates, long RowVersion, string? Instructions=null, int? IntroductorySets=null);
public record PlanInput(string Name, string? Description, long ExpectedVersion = 0);
public record TemplateInput(string Code, string Name, int Position, string? Notes, List<TemplateItemInput> Items, long ExpectedVersion = 0);
public record TemplateItemInput(string ExerciseId, int Position, int TargetSets, int? RepsMin, int? RepsMax, int? DurationSecondsMin, int? DurationSecondsMax, int RestSecondsMin, int RestSecondsMax, string RepetitionScope, List<string> Alternatives, string? RestScope=null, string? Notes=null);
public record TemplateDto(string Id, string Code, string Name, int Position, string? Notes, List<TemplateItemDto> Items, long RowVersion);
public record TemplateItemDto(string Id, string ExerciseId, int Position, int TargetSets, int? RepsMin, int? RepsMax, int? DurationSecondsMin, int? DurationSecondsMax, int RestSecondsMin, int RestSecondsMax, string RepetitionScope, List<string> Alternatives, string RestScope, string? Notes);
public record ArchiveResult(long Version, List<string> AffectedTemplates);
public record SyncOperation(string OperationId, string DeviceId, int SchemaVersion, string EntityId, string Kind, long BaseVersion, SessionEntry? Snapshot);
public record SyncAck(string OperationId, string EntityId, long Version, long Revision);
public sealed class ResourceNotFoundException() : Exception("Recurso não encontrado.");
public sealed class SyncConflictException(long version, SessionEntry? snapshot, bool deleted) : Exception("VERSION_CONFLICT")
{
    public long Version { get; } = version;
    public SessionEntry? Snapshot { get; } = snapshot;
    public bool Deleted { get; } = deleted;
}
public record SyncBootstrap(long Revision, List<ExerciseDto> Exercises, List<PlanDto> Plans, List<SessionEntry> Sessions, int HistoryDays);
public sealed class SyncChange
{
    public long? EntityVersion { get; set; } public long Revision { get; set; } public string EntityType { get; set; } = ""; public string? EntityId { get; set; } public string ChangeKind { get; set; } = ""; public string? PayloadJson { get; set; }
}
public record SyncPage(long NextCursor, bool HasMore, long ObservedRevision, List<SyncChange> Changes);

public interface ICurrentUser { string Id { get; } bool IsAdministrator => false; }
public interface ITrainingOwner { string OwnerId { get; } }
public interface ITrainingService
{
    Task<List<ExerciseDto>> Exercises(CancellationToken ct);
    Task<ExerciseDto?> Exercise(string id, CancellationToken ct);
    Task<ExerciseDto> SaveExercise(string? id, ExerciseInput input, CancellationToken ct);
    Task<ArchiveResult> DeleteExercise(string id, long expectedVersion, CancellationToken ct);
    Task<List<PlanDto>> Plans(CancellationToken ct);
    Task<string> SavePlan(string? id, PlanInput input, CancellationToken ct);
    Task<string> SaveTemplate(string? id, string planId, TemplateInput input, CancellationToken ct);
    Task<long> ArchiveDefinition(string kind,string id,long expectedVersion,CancellationToken ct);
    Task<List<SessionEntry>> Sessions(CancellationToken ct, string? from = null, string? to = null, int page = 1);
    Task<SessionEntry?> Session(string id, CancellationToken ct);
    Task<SyncBootstrap> Bootstrap(CancellationToken ct);
    Task<SyncPage> Changes(long after, int limit, CancellationToken ct);
    Task<SyncAck> Apply(SyncOperation operation, CancellationToken ct);
    Task<object> Frequency(string from, string to, CancellationToken ct);
    Task<List<ExerciseProgressPoint>> ExerciseProgress(string exerciseId, string from, string to, CancellationToken ct);
}
