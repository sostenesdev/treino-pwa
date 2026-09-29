using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Treinos.Application;
using Treinos.Infrastructure;

namespace Treinos.Api;

public sealed class AppUser
{
    public string Id { get; set; } = ""; public string Email { get; set; } = ""; public string DisplayName { get; set; } = ""; public string PasswordHash { get; set; } = ""; public string SecurityStamp { get; set; } = ""; public string AppRole { get; set; } = "common"; public bool MustChangePassword { get; set; } public string TimeZone { get; set; } = "America/Sao_Paulo"; public int AccessFailedCount { get; set; } public DateTime? LockoutEndUtc { get; set; }
}
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Id => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
}
public sealed class AccountService(Database database, IPasswordHasher<AppUser> hasher)
{
    public async Task<AppUser?> FindById(string id)
    {
        await using var db = await database.Open();
        return await db.QuerySingleOrDefaultAsync<AppUser>("SELECT id,email,display_name,password_hash,security_stamp,app_role,must_change_password,time_zone,access_failed_count,lockout_end_utc FROM app_users WHERE id=@id", new { id });
    }
    public async Task<AppUser?> FindByEmail(string email)
    {
        await using var db = await database.Open();
        return await db.QuerySingleOrDefaultAsync<AppUser>("SELECT id,email,display_name,password_hash,security_stamp,app_role,must_change_password,time_zone,access_failed_count,lockout_end_utc FROM app_users WHERE normalized_email=@email", new { email = email.Trim().ToUpperInvariant() });
    }
    public async Task<AppUser?> Login(string email, string password)
    {
        var user = await FindByEmail(email);
        if (user is null || user.LockoutEndUtc > DateTime.UtcNow) return null;
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        await using var db = await database.Open();
        if (result == PasswordVerificationResult.Failed)
        {
            await db.ExecuteAsync("UPDATE app_users SET access_failed_count=access_failed_count+1,lockout_end_utc=IF(access_failed_count>=4,DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 15 MINUTE),lockout_end_utc) WHERE id=@Id", new { user.Id });
            return null;
        }
        await db.ExecuteAsync("UPDATE app_users SET access_failed_count=0,lockout_end_utc=NULL WHERE id=@Id", new { user.Id });
        return user;
    }
    public async Task<Account> Create(string name, string email, string password, string role, bool change)
    {
        if (name.Trim().Length is < 2 or > 120 || !email.Contains('@') || password.Length is < 12 or > 128 || role is not ("common" or "administrator")) throw new ArgumentException("Dados da conta inválidos.");
        var id = Guid.NewGuid().ToString(); var stamp = Guid.NewGuid().ToString();
        var user = new AppUser { Id = id, Email = email.Trim(), DisplayName = name.Trim(), SecurityStamp = stamp, AppRole = role, MustChangePassword = change };
        var hash = hasher.HashPassword(user, password);
        await using var db = await database.Open(); await using var tx = await db.BeginTransactionAsync();
        await db.ExecuteAsync("INSERT INTO app_users(id,user_name,normalized_user_name,email,normalized_email,display_name,password_hash,security_stamp,concurrency_stamp,app_role,must_change_password) VALUES(@id,@email,@normalized,@email,@normalized,@name,@hash,@stamp,@concurrency,@role,@change)", new { id, email = user.Email, normalized = user.Email.ToUpperInvariant(), name = user.DisplayName, hash, stamp, concurrency = Guid.NewGuid().ToString(), role, change }, tx);
        await db.ExecuteAsync("INSERT INTO user_sync_state(user_id) VALUES(@id)", new { id }, tx);
        await tx.CommitAsync();
        return new(id, user.DisplayName, user.Email, role, change, user.TimeZone);
    }
    public async Task<bool> ResetWithStamp(string id, string oldStamp, string newPassword)
    {
        var user = await FindById(id);
        if (user is null || user.SecurityStamp != oldStamp || newPassword.Length is < 12 or > 128) return false;
        await using var db = await database.Open();
        return await db.ExecuteAsync("UPDATE app_users SET password_hash=@hash,security_stamp=@stamp,concurrency_stamp=@concurrency,must_change_password=FALSE,updated_at_utc=UTC_TIMESTAMP(6) WHERE id=@id AND security_stamp=@oldStamp", new { hash = hasher.HashPassword(user,newPassword), stamp = Guid.NewGuid().ToString(), concurrency = Guid.NewGuid().ToString(), id, oldStamp }) == 1;
    }
    public async Task<bool> ChangePassword(string id, string oldPassword, string newPassword)
    {
        var user = await FindById(id);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, oldPassword) == PasswordVerificationResult.Failed || newPassword.Length is < 12 or > 128) return false;
        var stamp = Guid.NewGuid().ToString();
        await using var db = await database.Open();
        return await db.ExecuteAsync("UPDATE app_users SET password_hash=@hash,security_stamp=@stamp,concurrency_stamp=@concurrency,must_change_password=FALSE,updated_at_utc=UTC_TIMESTAMP(6) WHERE id=@id AND security_stamp=@oldStamp", new { hash = hasher.HashPassword(user, newPassword), stamp, concurrency = Guid.NewGuid().ToString(), id, oldStamp = user.SecurityStamp }) == 1;
    }
}
