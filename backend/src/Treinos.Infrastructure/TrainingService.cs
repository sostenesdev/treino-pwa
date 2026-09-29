using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Treinos.Application;
using Treinos.Domain;

namespace Treinos.Infrastructure;

public sealed class Database(string connectionString)
{
    public async Task<MySqlConnection> Open(CancellationToken ct = default)
    {
        var settings = new MySqlConnectionStringBuilder(connectionString) { GuidFormat = MySqlGuidFormat.None };
        var db = new MySqlConnection(settings.ConnectionString);
        await db.OpenAsync(ct);
        return db;
    }
}

public sealed class TrainingService(Database database, ICurrentUser current) : ITrainingService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string UserId => current.Id;
    public async Task<List<ExerciseDto>> Exercises(CancellationToken ct)
    {
        await using var db = await database.Open(ct);
        return await ReadExercises(db);
    }
    private async Task<List<ExerciseDto>> ReadExercises(MySqlConnection db, MySqlTransaction? tx = null) =>
        (await db.QueryAsync<ExerciseDto>("SELECT id,name,muscle_group,equipment,measurement_type,load_kind,load_basis,instructions,row_version FROM exercises WHERE user_id=@UserId AND deleted_at_utc IS NULL ORDER BY name", new { UserId }, tx)).AsList();
    public async Task<ExerciseDto?> Exercise(string id, CancellationToken ct)
    {
        await using var db = await database.Open(ct);
        return await db.QuerySingleOrDefaultAsync<ExerciseDto>("SELECT id,name,muscle_group,equipment,measurement_type,load_kind,load_basis,instructions,row_version FROM exercises WHERE user_id=@UserId AND id=@id AND deleted_at_utc IS NULL", new { UserId, id });
    }
    public async Task<ExerciseDto> SaveExercise(string? id, ExerciseInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 || string.IsNullOrWhiteSpace(input.MuscleGroup) || input.MuscleGroup.Length > 80 || input.MeasurementType is not ("reps" or "duration") || input.LoadKind is not ("external" or "bodyweight" or "assisted") || input.LoadBasis is not ("total" or "per_hand" or "machine_display" or "added_weight")) throw new ArgumentException("Exercício inválido.");
        id ??= Guid.NewGuid().ToString();
        await using var db = await database.Open(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await LockRevision(db, tx);
        var sql = input.ExpectedVersion == 0 ?
            "INSERT INTO exercises(id,user_id,name,muscle_group,equipment,measurement_type,load_kind,load_basis,instructions) VALUES(@id,@UserId,@Name,@MuscleGroup,@Equipment,@MeasurementType,@LoadKind,@LoadBasis,@Instructions)" :
            "UPDATE exercises SET name=@Name,muscle_group=@MuscleGroup,equipment=@Equipment,measurement_type=@MeasurementType,load_kind=@LoadKind,load_basis=@LoadBasis,instructions=@Instructions,row_version=row_version+1,updated_at_utc=UTC_TIMESTAMP(6) WHERE id=@id AND user_id=@UserId AND row_version=@ExpectedVersion AND deleted_at_utc IS NULL";
        var affected = await db.ExecuteAsync(sql, new { id, UserId, input.Name, input.MuscleGroup, input.Equipment, input.MeasurementType, input.LoadKind, input.LoadBasis, input.Instructions, input.ExpectedVersion }, tx);
        if (affected != 1) throw new InvalidOperationException("Exercício ausente ou versão divergente.");
        var item = (await db.QuerySingleAsync<ExerciseDto>("SELECT id,name,muscle_group,equipment,measurement_type,load_kind,load_basis,instructions,row_version FROM exercises WHERE id=@id AND user_id=@UserId", new { id, UserId }, tx));
        await AddChange(db, tx, "exercise", id, "upsert", item.RowVersion, JsonSerializer.Serialize(item, Json));
        await tx.CommitAsync(ct);
        return item;
    }
    public async Task<bool> DeleteExercise(string id, long expectedVersion, CancellationToken ct)
    {
        await using var db = await database.Open(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await LockRevision(db, tx);
        var n = await db.ExecuteAsync("UPDATE exercises SET deleted_at_utc=UTC_TIMESTAMP(6),row_version=row_version+1 WHERE user_id=@UserId AND id=@id AND row_version=@expectedVersion AND deleted_at_utc IS NULL", new { UserId, id, expectedVersion }, tx);
        if (n == 1) await AddChange(db, tx, "exercise", id, "delete", expectedVersion + 1, null);
        await tx.CommitAsync(ct);
        return n == 1;
    }
    public async Task<List<PlanDto>> Plans(CancellationToken ct)
    {
        await using var db = await database.Open(ct);
        return await ReadPlans(db);
    }
    private async Task<List<PlanDto>> ReadPlans(MySqlConnection db, MySqlTransaction? tx = null)
    {
        var plans = (await db.QueryAsync<(string Id, string Name, string? Description, long RowVersion)>("SELECT id,name,description,row_version FROM workout_plans WHERE user_id=@UserId AND deleted_at_utc IS NULL ORDER BY name", new { UserId }, tx)).ToList();
        var result = new List<PlanDto>();
        foreach (var p in plans)
        {
            var templates = (await db.QueryAsync<(string Id, string Code, string Name, int Position, string? Notes, long RowVersion)>("SELECT id,code,name,position,notes,row_version FROM workout_templates WHERE user_id=@UserId AND plan_id=@Id AND deleted_at_utc IS NULL ORDER BY position", new { UserId, p.Id }, tx)).ToList();
            var list = new List<TemplateDto>();
            foreach (var t in templates)
            {
                var items = (await db.QueryAsync<TemplateItemRow>("SELECT id,exercise_id,position,target_sets,reps_min,reps_max,duration_seconds_min,duration_seconds_max,rest_seconds_min,rest_seconds_max,repetition_scope FROM workout_template_items WHERE user_id=@UserId AND template_id=@Id ORDER BY position", new { UserId, t.Id }, tx)).ToList();
                var dtos = new List<TemplateItemDto>();
                foreach (var i in items)
                {
                    var alternatives = (await db.QueryAsync<string>("SELECT exercise_id FROM workout_item_alternatives WHERE user_id=@UserId AND template_item_id=@Id", new { UserId, i.Id }, tx)).ToList();
                    dtos.Add(new(i.Id, i.ExerciseId, i.Position, i.TargetSets, i.RepsMin, i.RepsMax, i.DurationSecondsMin, i.DurationSecondsMax, i.RestSecondsMin, i.RestSecondsMax, i.RepetitionScope, alternatives));
                }
                list.Add(new(t.Id, t.Code, t.Name, t.Position, t.Notes, dtos, t.RowVersion));
            }
            result.Add(new(p.Id, p.Name, p.Description, list, p.RowVersion));
        }
        return result;
    }
    private sealed class TemplateItemRow
    {
        public string Id { get; set; } = ""; public string ExerciseId { get; set; } = ""; public int Position { get; set; } public int TargetSets { get; set; }
        public int? RepsMin { get; set; } public int? RepsMax { get; set; } public int? DurationSecondsMin { get; set; } public int? DurationSecondsMax { get; set; }
        public int RestSecondsMin { get; set; } public int RestSecondsMax { get; set; } public string RepetitionScope { get; set; } = "total";
    }
    public async Task<string> SavePlan(string? id, PlanInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160) throw new ArgumentException("Nome do plano inválido.");
        id ??= Guid.NewGuid().ToString();
        await using var db = await database.Open(ct); await using var tx = await db.BeginTransactionAsync(ct);
        await LockRevision(db, tx);
        var n = input.ExpectedVersion == 0 ? await db.ExecuteAsync("INSERT INTO workout_plans(id,user_id,name,description) VALUES(@id,@UserId,@Name,@Description)", new { id, UserId, input.Name, input.Description }, tx)
            : await db.ExecuteAsync("UPDATE workout_plans SET name=@Name,description=@Description,row_version=row_version+1,updated_at_utc=UTC_TIMESTAMP(6) WHERE id=@id AND user_id=@UserId AND row_version=@ExpectedVersion AND deleted_at_utc IS NULL", new { id, UserId, input.Name, input.Description, input.ExpectedVersion }, tx);
        if (n != 1) throw new InvalidOperationException("Plano ausente ou versão divergente.");
        await AddChange(db, tx, "plan", id, "upsert", input.ExpectedVersion + 1, JsonSerializer.Serialize(new { id, input.Name, input.Description, rowVersion = input.ExpectedVersion + 1 }, Json));
        await tx.CommitAsync(ct); return id;
    }
    public async Task<string> SaveTemplate(string? id, string planId, TemplateInput input, CancellationToken ct)
    {
        if (!Guid.TryParse(planId, out _) || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 20 || input.Position < 1 || input.Items.Count == 0 || input.Items.Select(x => x.Position).Distinct().Count() != input.Items.Count) throw new ArgumentException("Ficha inválida.");
        foreach (var item in input.Items)
            if (!Guid.TryParse(item.ExerciseId, out _) || item.Position < 1 || item.TargetSets < 1 || item.RestSecondsMin < 0 || item.RestSecondsMax < item.RestSecondsMin || item.RepetitionScope is not ("total" or "per_side") ||
                !((item.RepsMin is > 0 && item.RepsMax >= item.RepsMin && item.DurationSecondsMin is null && item.DurationSecondsMax is null) || (item.DurationSecondsMin is > 0 && item.DurationSecondsMax >= item.DurationSecondsMin && item.RepsMin is null && item.RepsMax is null))) throw new ArgumentException("Prescrição inválida.");
        id ??= Guid.NewGuid().ToString();
        await using var db = await database.Open(ct); await using var tx = await db.BeginTransactionAsync(ct);
        await LockRevision(db, tx);
        if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM workout_plans WHERE user_id=@UserId AND id=@planId AND deleted_at_utc IS NULL", new { UserId, planId }, tx) != 1) throw new ArgumentException("Plano ausente.");
        foreach (var exerciseId in input.Items.SelectMany(x => x.Alternatives.Append(x.ExerciseId)).Distinct())
            if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM exercises WHERE user_id=@UserId AND id=@exerciseId AND deleted_at_utc IS NULL", new { UserId, exerciseId }, tx) != 1) throw new ArgumentException("Exercício ausente ou arquivado.");
        var n = input.ExpectedVersion == 0 ? await db.ExecuteAsync("INSERT INTO workout_templates(id,user_id,plan_id,code,name,position,notes) VALUES(@id,@UserId,@planId,@Code,@Name,@Position,@Notes)", new { id, UserId, planId, input.Code, input.Name, input.Position, input.Notes }, tx)
            : await db.ExecuteAsync("UPDATE workout_templates SET code=@Code,name=@Name,position=@Position,notes=@Notes,row_version=row_version+1 WHERE id=@id AND user_id=@UserId AND plan_id=@planId AND row_version=@ExpectedVersion AND deleted_at_utc IS NULL", new { id, UserId, planId, input.Code, input.Name, input.Position, input.Notes, input.ExpectedVersion }, tx);
        if (n != 1) throw new InvalidOperationException("Ficha ausente ou versão divergente.");
        if (input.ExpectedVersion != 0) await db.ExecuteAsync("DELETE FROM workout_template_items WHERE user_id=@UserId AND template_id=@id", new { UserId, id }, tx);
        foreach (var item in input.Items)
        {
            var itemId = Guid.NewGuid().ToString();
            await db.ExecuteAsync("INSERT INTO workout_template_items(id,user_id,template_id,exercise_id,position,target_sets,reps_min,reps_max,duration_seconds_min,duration_seconds_max,repetition_scope,rest_seconds_min,rest_seconds_max,notes) VALUES(@itemId,@UserId,@id,@ExerciseId,@Position,@TargetSets,@RepsMin,@RepsMax,@DurationSecondsMin,@DurationSecondsMax,@RepetitionScope,@RestSecondsMin,@RestSecondsMax,NULL)", new { itemId, UserId, id, item.ExerciseId, item.Position, item.TargetSets, item.RepsMin, item.RepsMax, item.DurationSecondsMin, item.DurationSecondsMax, item.RepetitionScope, item.RestSecondsMin, item.RestSecondsMax }, tx);
            foreach (var alternative in item.Alternatives.Distinct()) await db.ExecuteAsync("INSERT INTO workout_item_alternatives(user_id,template_item_id,exercise_id) VALUES(@UserId,@itemId,@alternative)", new { UserId, itemId, alternative }, tx);
        }
        await AddChange(db, tx, "template", id, "upsert", input.ExpectedVersion + 1, JsonSerializer.Serialize(new { id, planId, input.Code, input.Name, input.Position, input.Notes, input.Items, rowVersion = input.ExpectedVersion + 1 }, Json));
        await tx.CommitAsync(ct); return id;
    }
    public async Task<List<SessionEntry>> Sessions(CancellationToken ct)
    {
        await using var db = await database.Open(ct);
        return await ReadSessions(db, ct);
    }
    private async Task<List<SessionEntry>> ReadSessions(MySqlConnection db, CancellationToken ct, MySqlTransaction? tx = null, int historyDays = 90, DateOnly? fromDate = null)
    {
        var start = (fromDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-historyDays)).ToString("yyyy-MM-dd");
        var rows = (await db.QueryAsync<SessionRow>("SELECT id,template_id,workout_name_snapshot,DATE_FORMAT(performed_on,'%Y-%m-%d') performed_on,time_zone_snapshot,status,recording_mode,notes,row_version FROM workout_sessions WHERE user_id=@UserId AND deleted_at_utc IS NULL AND performed_on>=@start ORDER BY performed_on DESC", new { UserId, start }, tx)).ToList();
        var result = new List<SessionEntry>();
        foreach (var row in rows)
        {
            var exercises = (await db.QueryAsync<SessionExerciseRow>("SELECT id,exercise_id,position,exercise_name_snapshot,measurement_type_snapshot,load_kind_snapshot,load_basis_snapshot,repetition_scope_snapshot,completion_status FROM session_exercises WHERE user_id=@UserId AND session_id=@Id ORDER BY position", new { UserId, row.Id }, tx)).ToList();
            var list = new List<ExerciseEntry>();
            foreach (var ex in exercises)
            {
                var sets = (await db.QueryAsync<SetEntry>("SELECT id,set_number,side,repetitions,duration_seconds,load_kg,rir,is_warmup,is_completed FROM session_sets WHERE user_id=@UserId AND session_exercise_id=@Id ORDER BY set_number,side", new { UserId, ex.Id }, tx)).ToList();
                list.Add(new(ex.Id, ex.ExerciseId, ex.Position, ex.ExerciseNameSnapshot, ex.MeasurementTypeSnapshot, ex.LoadKindSnapshot, ex.LoadBasisSnapshot, ex.RepetitionScopeSnapshot, ex.CompletionStatus, sets));
            }
            result.Add(new(row.Id, row.TemplateId, row.WorkoutNameSnapshot, row.PerformedOn, row.TimeZoneSnapshot, row.Status, row.RecordingMode, row.Notes, list, row.RowVersion));
        }
        return result;
    }
    private sealed class SessionRow
    {
        public string Id { get; set; } = ""; public string? TemplateId { get; set; } public string WorkoutNameSnapshot { get; set; } = ""; public string PerformedOn { get; set; } = ""; public string TimeZoneSnapshot { get; set; } = ""; public string Status { get; set; } = ""; public string RecordingMode { get; set; } = ""; public string? Notes { get; set; } public long RowVersion { get; set; }
    }
    private sealed class SessionExerciseRow
    {
        public string Id { get; set; } = ""; public string ExerciseId { get; set; } = ""; public int Position { get; set; } public string ExerciseNameSnapshot { get; set; } = ""; public string MeasurementTypeSnapshot { get; set; } = ""; public string LoadKindSnapshot { get; set; } = ""; public string LoadBasisSnapshot { get; set; } = ""; public string RepetitionScopeSnapshot { get; set; } = ""; public string CompletionStatus { get; set; } = "";
    }
    public async Task<SyncBootstrap> Bootstrap(CancellationToken ct)
    {
        await using var db = await database.Open(ct);
        await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var revision = await db.ExecuteScalarAsync<long>("SELECT current_revision FROM user_sync_state WHERE user_id=@UserId", new { UserId }, tx);
        var result = new SyncBootstrap(revision, await ReadExercises(db, tx), await ReadPlans(db, tx), await ReadSessions(db, ct, tx), 90);
        await tx.CommitAsync(ct);
        return result;
    }
    public async Task<SyncPage> Changes(long after, int limit, CancellationToken ct)
    {
        await using var db = await database.Open(ct);
        var state = await db.QuerySingleAsync<(long CurrentRevision, long MinAvailableRevision)>("SELECT current_revision,min_available_revision FROM user_sync_state WHERE user_id=@UserId", new { UserId });
        if (after < state.MinAvailableRevision - 1) throw new InvalidOperationException("SYNC_CURSOR_EXPIRED");
        var changes = (await db.QueryAsync<SyncChange>("SELECT revision,entity_type,entity_id,change_kind,CAST(payload_json AS CHAR) payload_json FROM sync_changes WHERE user_id=@UserId AND revision>@after ORDER BY revision LIMIT @limit", new { UserId, after, limit = Math.Clamp(limit, 1, 100) })).ToList();
        var next = changes.Count == 0 ? after : changes[^1].Revision;
        return new(next, next < state.CurrentRevision, state.CurrentRevision, changes);
    }
    private static (DateOnly From, DateOnly To) Period(string from, string to)
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var start) || !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var end) || end < start || end.DayNumber - start.DayNumber > 365) throw new ArgumentException("Período inválido ou maior que 366 dias.");
        return (start, end);
    }
    public async Task<object> Frequency(string from, string to, CancellationToken ct)
    {
        var period = Period(from, to); await using var db = await database.Open(ct);
        var done = (await ReadSessions(db, ct, fromDate: period.From)).Where(s => s.Status == "completed" && DateOnly.Parse(s.PerformedOn) <= period.To).ToList();
        return new { sessions = done.Count, distinctDays = done.Select(s => s.PerformedOn).Distinct().Count(), byMonth = done.GroupBy(s => s.PerformedOn[..7]).Select(g => new { month = g.Key, sessions = g.Count() }).OrderBy(x => x.month) };
    }
    public async Task<List<ExerciseProgressPoint>> ExerciseProgress(string exerciseId, string from, string to, CancellationToken ct)
    {
        var period = Period(from, to); await using var db = await database.Open(ct);
        var done = (await ReadSessions(db, ct, fromDate: period.From)).Where(s => s.Status == "completed" && DateOnly.Parse(s.PerformedOn) <= period.To);
        return done.Select(s => ProgressRules.Calculate(s.PerformedOn, s.Exercises.Where(e => e.ExerciseId == exerciseId && e.CompletionStatus == "completed"))).Where(x => x.EligibleSets > 0 || x.DurationSeconds > 0).OrderBy(x => x.PerformedOn).ToList();
    }
    public async Task<SyncAck> Apply(SyncOperation op, CancellationToken ct)
    {
        if (!Guid.TryParse(op.OperationId, out _) || !Guid.TryParse(op.DeviceId, out _) || !Guid.TryParse(op.EntityId, out _) || op.SchemaVersion != 1 || op.Kind is not ("create" or "replace" or "delete")) throw new ArgumentException("Operação de sync inválida.");
        if (op.Kind != "delete" && (op.Snapshot is null || op.Snapshot.Id != op.EntityId)) throw new ArgumentException("Snapshot inválido.");
        if (op.Snapshot is not null) WorkoutRules.Validate(op.Snapshot);
        var payload = JsonSerializer.Serialize(op, Json);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        await using var db = await database.Open(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await LockRevision(db, tx);
        var receipt = await db.QuerySingleOrDefaultAsync<(string RequestHash, string ResponseJson)>("SELECT request_hash,response_json FROM sync_operation_receipts WHERE user_id=@UserId AND operation_id=@OperationId", new { UserId, op.OperationId }, tx);
        if (receipt.RequestHash is not null)
        {
            if (receipt.RequestHash != hash) throw new InvalidOperationException("IDEMPOTENCY_KEY_REUSED");
            return JsonSerializer.Deserialize<SyncAck>(receipt.ResponseJson, Json)!;
        }
        var version = await db.ExecuteScalarAsync<long?>("SELECT row_version FROM workout_sessions WHERE user_id=@UserId AND id=@EntityId AND deleted_at_utc IS NULL", new { UserId, op.EntityId }, tx);
        if (op.Kind == "create" ? version is not null || op.BaseVersion != 0 : version is null || version != op.BaseVersion) throw new InvalidOperationException("VERSION_CONFLICT");
        if (op.Kind == "create")
        {
            var s = op.Snapshot!;
            await db.ExecuteAsync("INSERT INTO workout_sessions(id,user_id,template_id,workout_name_snapshot,performed_on,time_zone_snapshot,status,recording_mode,notes,completed_at_utc,client_request_id,request_hash) VALUES(@Id,@UserId,@TemplateId,@WorkoutNameSnapshot,@PerformedOn,@TimeZoneSnapshot,@Status,@RecordingMode,@Notes,CASE WHEN @Status='completed' THEN UTC_TIMESTAMP(6) ELSE NULL END,@OperationId,@hash)", new { s.Id, UserId, s.TemplateId, s.WorkoutNameSnapshot, s.PerformedOn, s.TimeZoneSnapshot, s.Status, s.RecordingMode, s.Notes, op.OperationId, hash }, tx);
        }
        else if (op.Kind == "delete") await db.ExecuteAsync("UPDATE workout_sessions SET deleted_at_utc=UTC_TIMESTAMP(6),row_version=row_version+1 WHERE user_id=@UserId AND id=@EntityId", new { UserId, op.EntityId }, tx);
        else
        {
            var s = op.Snapshot!;
            await db.ExecuteAsync("UPDATE workout_sessions SET workout_name_snapshot=@WorkoutNameSnapshot,performed_on=@PerformedOn,status=@Status,recording_mode=@RecordingMode,notes=@Notes,completed_at_utc=CASE WHEN @Status='completed' THEN UTC_TIMESTAMP(6) ELSE NULL END,row_version=row_version+1 WHERE user_id=@UserId AND id=@Id", new { s.Id, UserId, s.WorkoutNameSnapshot, s.PerformedOn, s.Status, s.RecordingMode, s.Notes }, tx);
            await db.ExecuteAsync("DELETE ss FROM session_sets ss JOIN session_exercises se ON se.user_id=ss.user_id AND se.id=ss.session_exercise_id WHERE se.user_id=@UserId AND se.session_id=@EntityId", new { UserId, op.EntityId }, tx);
            await db.ExecuteAsync("DELETE FROM session_exercises WHERE user_id=@UserId AND session_id=@EntityId", new { UserId, op.EntityId }, tx);
        }
        if (op.Kind != "delete")
        {
            foreach (var ex in op.Snapshot!.Exercises)
            {
                var owned = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM exercises WHERE user_id=@UserId AND id=@ExerciseId", new { UserId, ex.ExerciseId }, tx);
                if (owned != 1) throw new ArgumentException("Exercício não pertence à conta.");
                await db.ExecuteAsync("INSERT INTO session_exercises(id,user_id,session_id,exercise_id,position,exercise_name_snapshot,measurement_type_snapshot,load_kind_snapshot,load_basis_snapshot,load_unit_snapshot,repetition_scope_snapshot,completion_status) VALUES(@Id,@UserId,@EntityId,@ExerciseId,@Position,@ExerciseNameSnapshot,@MeasurementTypeSnapshot,@LoadKindSnapshot,@LoadBasisSnapshot,'kg',@RepetitionScopeSnapshot,@CompletionStatus)", new { ex.Id, UserId, op.EntityId, ex.ExerciseId, ex.Position, ex.ExerciseNameSnapshot, ex.MeasurementTypeSnapshot, ex.LoadKindSnapshot, ex.LoadBasisSnapshot, ex.RepetitionScopeSnapshot, ex.CompletionStatus }, tx);
                foreach (var set in ex.Sets) await db.ExecuteAsync("INSERT INTO session_sets(id,user_id,session_exercise_id,set_number,side,repetitions,duration_seconds,load_kg,rir,is_warmup,is_completed) VALUES(@Id,@UserId,@ExerciseId,@SetNumber,@Side,@Repetitions,@DurationSeconds,@LoadKg,@Rir,@IsWarmup,@IsCompleted)", new { set.Id, UserId, ExerciseId = ex.Id, set.SetNumber, set.Side, set.Repetitions, set.DurationSeconds, set.LoadKg, set.Rir, set.IsWarmup, set.IsCompleted }, tx);
            }
        }
        var newVersion = (version ?? 0) + 1;
        var revision = await AddChange(db, tx, "session", op.EntityId, op.Kind == "delete" ? "delete" : "upsert", newVersion, op.Kind == "delete" ? null : JsonSerializer.Serialize(op.Snapshot! with { RowVersion = newVersion }, Json));
        var ack = new SyncAck(op.OperationId, op.EntityId, newVersion, revision);
        await db.ExecuteAsync("INSERT INTO sync_operation_receipts(user_id,operation_id,device_id,entity_id,operation_kind,request_hash,response_json,applied_revision) VALUES(@UserId,@OperationId,@DeviceId,@EntityId,@Kind,@hash,@response,@revision)", new { UserId, op.OperationId, op.DeviceId, op.EntityId, op.Kind, hash, response = JsonSerializer.Serialize(ack, Json), revision }, tx);
        await tx.CommitAsync(ct);
        return ack;
    }
    private async Task LockRevision(MySqlConnection db, MySqlTransaction tx) => _ = await db.ExecuteScalarAsync<long>("SELECT current_revision FROM user_sync_state WHERE user_id=@UserId FOR UPDATE", new { UserId }, tx);
    private async Task<long> AddChange(MySqlConnection db, MySqlTransaction tx, string type, string id, string kind, long version, string? payload)
    {
        await db.ExecuteAsync("UPDATE user_sync_state SET current_revision=current_revision+1 WHERE user_id=@UserId", new { UserId }, tx);
        var revision = await db.ExecuteScalarAsync<long>("SELECT current_revision FROM user_sync_state WHERE user_id=@UserId", new { UserId }, tx);
        await db.ExecuteAsync("INSERT INTO sync_changes(user_id,revision,entity_type,entity_id,change_kind,entity_version,payload_json) VALUES(@UserId,@revision,@type,@id,@kind,@version,@payload)", new { UserId, revision, type, id, kind, version, payload }, tx);
        return revision;
    }
}
