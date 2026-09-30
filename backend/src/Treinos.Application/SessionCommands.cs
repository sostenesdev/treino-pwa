using Treinos.Domain;

namespace Treinos.Application;

// The caller retains this complete command until acknowledged. Retrying a command
// must not rebuild its snapshot from a newer server version.
public record SessionCommand(string OperationId, string DeviceId, long BaseVersion, SessionEntry? Snapshot);

public sealed class SessionCommands(ITrainingService service)
{
    public Task<SyncAck> Execute(string action, string id, SessionCommand command, string? itemId, string? setId, CancellationToken ct)
    {
        if (command.Snapshot is { } snapshot && snapshot.Id != id)
            throw new ArgumentException("A sessão do comando deve corresponder à rota.");
        if (action != "delete" && command.Snapshot is null)
            throw new ArgumentException("Envie o snapshot completo com a versão esperada.");
        var value = command.Snapshot;
        if (itemId is not null && !Guid.TryParse(itemId, out _)) throw new ArgumentException("Item inválido.");
        if (setId is not null && !Guid.TryParse(setId, out _)) throw new ArgumentException("Série inválida.");
        if (action is "exercise" or "set" or "remove-exercise" or "remove-set")
        {
            var item = value!.Exercises.SingleOrDefault(e => e.Id == itemId) ?? throw new ArgumentException("Item ausente do snapshot.");
            if (action is ("set" or "remove-set") && !item.Sets.Any(s => s.Id == setId)) throw new ArgumentException("Série ausente do snapshot.");
        }
        if (action == "remove-exercise") value = value! with { Exercises = value.Exercises.Where(e => e.Id != itemId).ToList() };
        if (action == "remove-set") value = value! with { Exercises = value.Exercises.Select(e => e.Id == itemId ? e with { Sets = e.Sets.Where(s => s.Id != setId).ToList() } : e).ToList() };
        if (action == "complete") value = value! with { Status = "completed" };
        return service.Apply(new(command.OperationId, command.DeviceId, 1, id,
            action == "delete" ? "delete" : action == "create" ? "create" : "replace", command.BaseVersion, action == "delete" ? null : value), ct);
    }
}
