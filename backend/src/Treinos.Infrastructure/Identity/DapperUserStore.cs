using Dapper;
using Microsoft.AspNetCore.Identity;
using MySqlConnector;

namespace Treinos.Infrastructure.Identity;

public sealed class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string AppRole { get; set; } = "common";
    public bool MustChangePassword { get; set; }
    public string TimeZone { get; set; } = "America/Sao_Paulo";
}

public sealed class DapperUserStore(Database database, IdentityErrorDescriber errors) :
    IUserPasswordStore<AppUser>, IUserEmailStore<AppUser>, IUserSecurityStampStore<AppUser>, IUserLockoutStore<AppUser>
{
    private const string Select = "SELECT id,user_name,normalized_user_name,email,normalized_email,display_name,password_hash,security_stamp,concurrency_stamp,email_confirmed,lockout_enabled,lockout_end_utc,access_failed_count,app_role,must_change_password,time_zone FROM app_users";
    public void Dispose() { }
    public async Task<IdentityResult> CreateAsync(AppUser user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var db = await database.Open(ct); await using var tx = await db.BeginTransactionAsync(ct);
        try
        {
            await db.ExecuteAsync(new CommandDefinition("INSERT INTO app_users(id,user_name,normalized_user_name,email,normalized_email,display_name,password_hash,security_stamp,concurrency_stamp,email_confirmed,lockout_enabled,lockout_end_utc,access_failed_count,app_role,must_change_password,time_zone) VALUES(@Id,@UserName,@NormalizedUserName,@Email,@NormalizedEmail,@DisplayName,@PasswordHash,@SecurityStamp,@ConcurrencyStamp,@EmailConfirmed,@LockoutEnabled,@LockoutEnd,@AccessFailedCount,@AppRole,@MustChangePassword,@TimeZone)", Parameters(user), tx, cancellationToken:ct));
            await db.ExecuteAsync(new CommandDefinition("INSERT INTO user_sync_state(user_id) VALUES(@Id)", new {user.Id}, tx, cancellationToken:ct));
            await tx.CommitAsync(ct); return IdentityResult.Success;
        }
        catch (MySqlException e) when(e.Number==1062) { return IdentityResult.Failed(errors.DuplicateEmail(user.Email!)); }
    }
    public async Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var oldStamp = user.ConcurrencyStamp; var newStamp = Guid.NewGuid().ToString();
        var parameters = Parameters(user); parameters.Add("OldStamp", oldStamp); parameters.Add("NewStamp", newStamp);
        await using var db = await database.Open(ct);
        try
        {
            var count = await db.ExecuteAsync(new CommandDefinition("UPDATE app_users SET user_name=@UserName,normalized_user_name=@NormalizedUserName,email=@Email,normalized_email=@NormalizedEmail,display_name=@DisplayName,password_hash=@PasswordHash,security_stamp=@SecurityStamp,concurrency_stamp=@NewStamp,email_confirmed=@EmailConfirmed,lockout_enabled=@LockoutEnabled,lockout_end_utc=@LockoutEnd,access_failed_count=@AccessFailedCount,app_role=@AppRole,must_change_password=@MustChangePassword,time_zone=@TimeZone,row_version=row_version+1,updated_at_utc=UTC_TIMESTAMP(6) WHERE id=@Id AND concurrency_stamp=@OldStamp", parameters, cancellationToken:ct));
            if(count!=1) return IdentityResult.Failed(errors.ConcurrencyFailure());
            user.ConcurrencyStamp=newStamp;return IdentityResult.Success;
        }
        catch(MySqlException e) when(e.Number==1062) { return IdentityResult.Failed(errors.DuplicateEmail(user.Email!)); }
    }
    private static DynamicParameters Parameters(AppUser user)
    {
        var p=new DynamicParameters(user); p.Add("LockoutEnd",user.LockoutEnd?.UtcDateTime); return p;
    }
    public Task<IdentityResult> DeleteAsync(AppUser user,CancellationToken ct) => Task.FromResult(IdentityResult.Failed(new IdentityError{Code="DeletionNotSupported",Description="Contas com dados não são excluídas pelo aplicativo."}));
    private async Task<AppUser?> Find(string condition,object parameters,CancellationToken ct)
    {
        await using var db=await database.Open(ct);
        var row=await db.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(Select+" WHERE "+condition,parameters,cancellationToken:ct));
        return row?.ToUser();
    }
    public Task<AppUser?> FindByIdAsync(string id,CancellationToken ct)=>Find("id=@id",new{id},ct);
    public Task<AppUser?> FindByNameAsync(string name,CancellationToken ct)=>Find("normalized_user_name=@name",new{name},ct);
    public Task<AppUser?> FindByEmailAsync(string email,CancellationToken ct)=>Find("normalized_email=@email",new{email},ct);
    public Task<string> GetUserIdAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.Id);
    public Task<string?> GetUserNameAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.UserName);
    public Task SetUserNameAsync(AppUser u,string? value,CancellationToken ct){u.UserName=value;return Task.CompletedTask;}
    public Task<string?> GetNormalizedUserNameAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(AppUser u,string? value,CancellationToken ct){u.NormalizedUserName=value;return Task.CompletedTask;}
    public Task SetPasswordHashAsync(AppUser u,string? value,CancellationToken ct){u.PasswordHash=value;return Task.CompletedTask;}
    public Task<string?> GetPasswordHashAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.PasswordHash);
    public Task<bool> HasPasswordAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.PasswordHash!=null);
    public Task SetEmailAsync(AppUser u,string? value,CancellationToken ct){u.Email=value;return Task.CompletedTask;}
    public Task<string?> GetEmailAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.Email);
    public Task<bool> GetEmailConfirmedAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.EmailConfirmed);
    public Task SetEmailConfirmedAsync(AppUser u,bool value,CancellationToken ct){u.EmailConfirmed=value;return Task.CompletedTask;}
    public Task<string?> GetNormalizedEmailAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.NormalizedEmail);
    public Task SetNormalizedEmailAsync(AppUser u,string? value,CancellationToken ct){u.NormalizedEmail=value;return Task.CompletedTask;}
    public Task SetSecurityStampAsync(AppUser u,string value,CancellationToken ct){u.SecurityStamp=value;return Task.CompletedTask;}
    public Task<string?> GetSecurityStampAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.SecurityStamp);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.LockoutEnd);
    public Task SetLockoutEndDateAsync(AppUser u,DateTimeOffset? value,CancellationToken ct){u.LockoutEnd=value;return Task.CompletedTask;}
    public Task<int> IncrementAccessFailedCountAsync(AppUser u,CancellationToken ct)=>Task.FromResult(++u.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(AppUser u,CancellationToken ct){u.AccessFailedCount=0;return Task.CompletedTask;}
    public Task<int> GetAccessFailedCountAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(AppUser u,CancellationToken ct)=>Task.FromResult(u.LockoutEnabled);
    public Task SetLockoutEnabledAsync(AppUser u,bool value,CancellationToken ct){u.LockoutEnabled=value;return Task.CompletedTask;}
    private sealed class UserRow
    {
        public string Id{get;set;}="";public string UserName{get;set;}="";public string NormalizedUserName{get;set;}="";public string Email{get;set;}="";public string NormalizedEmail{get;set;}="";public string DisplayName{get;set;}="";public string PasswordHash{get;set;}="";public string SecurityStamp{get;set;}="";public string ConcurrencyStamp{get;set;}="";public bool EmailConfirmed{get;set;}public bool LockoutEnabled{get;set;}public DateTime? LockoutEndUtc{get;set;}public int AccessFailedCount{get;set;}public string AppRole{get;set;}="common";public bool MustChangePassword{get;set;}public string TimeZone{get;set;}="America/Sao_Paulo";
        public AppUser ToUser()=>new(){Id=Id,UserName=UserName,NormalizedUserName=NormalizedUserName,Email=Email,NormalizedEmail=NormalizedEmail,DisplayName=DisplayName,PasswordHash=PasswordHash,SecurityStamp=SecurityStamp,ConcurrencyStamp=ConcurrencyStamp,EmailConfirmed=EmailConfirmed,LockoutEnabled=LockoutEnabled,LockoutEnd=LockoutEndUtc is {} date?new DateTimeOffset(DateTime.SpecifyKind(date,DateTimeKind.Utc)):null,AccessFailedCount=AccessFailedCount,AppRole=AppRole,MustChangePassword=MustChangePassword,TimeZone=TimeZone};
    }
}
