using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Treinos.Infrastructure;

public sealed class RetentionMaintenance(Database database, ILogger<RetentionMaintenance> logger) : BackgroundService
{
    public static async Task Prune(Database database, DateTime cutoffUtc, CancellationToken ct)
    {
        await using var db = await database.Open(ct);
        var users = (await db.QueryAsync<string>("SELECT DISTINCT user_id FROM sync_changes WHERE created_at_utc<@cutoffUtc", new { cutoffUtc })).ToList();
        foreach (var userId in users)
        {
            await using var tx = await db.BeginTransactionAsync(ct);
            await db.ExecuteScalarAsync<long>("SELECT current_revision FROM user_sync_state WHERE user_id=@userId FOR UPDATE", new { userId }, tx);
            await db.ExecuteAsync("DELETE FROM sync_changes WHERE user_id=@userId AND created_at_utc<@cutoffUtc", new { userId, cutoffUtc }, tx);
            await db.ExecuteAsync("UPDATE user_sync_state SET min_available_revision=COALESCE((SELECT MIN(revision) FROM sync_changes WHERE user_id=@userId),current_revision+1) WHERE user_id=@userId", new { userId }, tx);
            await tx.CommitAsync(ct);
        }
        await db.ExecuteAsync("UPDATE email_outbox SET status='expired',payload_ciphertext=X'',lease_token=NULL,lease_until_utc=NULL WHERE status IN ('pending','processing','failed') AND token_expires_at_utc<=UTC_TIMESTAMP(6)");
        await db.ExecuteAsync("DELETE FROM email_outbox WHERE status IN ('sent','expired','failed') AND created_at_utc<DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 30 DAY)");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await Prune(database, DateTime.UtcNow.AddDays(-90), ct); }
            catch (Exception e) when (!ct.IsCancellationRequested) { logger.LogWarning("Retenção: {ErrorType}", e.GetType().Name); }
        }
    }
}
