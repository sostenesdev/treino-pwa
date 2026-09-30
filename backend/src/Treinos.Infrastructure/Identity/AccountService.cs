using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Treinos.Application;

namespace Treinos.Infrastructure.Identity;

public sealed class AccountService(UserManager<AppUser> users,IUserClaimsPrincipalFactory<AppUser> principals,ICurrentUser current,Database database) : IAccountService
{
    public async Task<Account?> GetCurrent(CancellationToken ct=default) { ct.ThrowIfCancellationRequested();var user=await users.FindByIdAsync(current.Id);return user is null?null:View(user); }
    public async Task<Account?> ValidateSession(string id,string stamp,string role,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();var user=await users.FindByIdAsync(id);
        return user is not null&&user.SecurityStamp==stamp&&user.AppRole==role&&!await users.IsLockedOutAsync(user)?View(user):null;
    }
    public async Task<AuthenticationResult?> Login(string email,string password,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();var user=await users.FindByEmailAsync(email.Trim());
        if(user is null||await users.IsLockedOutAsync(user))return null;
        if(!await users.CheckPasswordAsync(user,password)){await users.AccessFailedAsync(user);return null;}
        var reset=await users.ResetAccessFailedCountAsync(user);if(!reset.Succeeded)return null;
        return new(View(user),await principals.CreateAsync(user));
    }
    public async Task<Account> CreateCommonUser(string name,string email,string initialPassword,CancellationToken ct=default)
    {
        var actor=await users.FindByIdAsync(current.Id);
        if(actor is null||actor.AppRole!="administrator"||actor.MustChangePassword||await users.IsLockedOutAsync(actor))throw new UnauthorizedAccessException();
        return await Create(name,email,initialPassword,"common",true,ct);
    }
    private async Task<Account> Create(string name,string email,string password,string role,bool change,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(string.IsNullOrWhiteSpace(name)||name.Trim().Length is <2 or >120)throw new ArgumentException("Nome inválido.");
        var user=new AppUser{Id=Guid.NewGuid().ToString(),UserName=email.Trim(),Email=email.Trim(),DisplayName=name.Trim(),AppRole=role,MustChangePassword=change};
        Ensure(await users.CreateAsync(user,password));return View(user);
    }
    public async Task<bool> ChangePassword(string oldPassword,string newPassword,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();var user=await users.FindByIdAsync(current.Id);if(user is null)return false;
        user.MustChangePassword=false;
        return (await users.ChangePasswordAsync(user,oldPassword,newPassword)).Succeeded;
    }
    public async Task<Account> BootstrapAdministrator(string name,string email,string password,string? promoteId=null,CancellationToken ct=default)
    {
        await using var db=await database.Open(ct);
        if(await db.ExecuteScalarAsync<int>("SELECT GET_LOCK('treinos_bootstrap_administrator',30)")!=1)throw new InvalidOperationException("Bootstrap em uso.");
        try
        {
            var existing=await users.FindByEmailAsync(email.Trim());
            if(existing is null)return await Create(name,email,password,"administrator",false,ct);
            if(existing.AppRole=="administrator")return View(existing);
            if(promoteId!=existing.Id)throw new InvalidOperationException("A promoção exige o ID explícito da conta existente.");
            existing.AppRole="administrator";Ensure(await users.UpdateSecurityStampAsync(existing));return View(existing);
        }
        finally{await db.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK('treinos_bootstrap_administrator')");}
    }
    private static void Ensure(IdentityResult result){if(!result.Succeeded)throw new ArgumentException(string.Join(" ",result.Errors.Select(e=>e.Description)));}
    private static Account View(AppUser user)=>new(user.Id,user.DisplayName,user.Email!,user.AppRole,user.MustChangePassword,user.TimeZone);
}

public sealed class PrincipalFactory(UserManager<AppUser> users,IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<AppUser>(users,options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity=await base.GenerateClaimsAsync(user);identity.AddClaim(new(ClaimTypes.Role,user.AppRole));identity.AddClaim(new("security_stamp",user.SecurityStamp!));return identity;
    }
}
public sealed class PasswordLengthValidator : IPasswordValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager,AppUser user,string? password)=>Task.FromResult(password is {Length: >=12 and <=128}?IdentityResult.Success:IdentityResult.Failed(new IdentityError{Code="PasswordLength",Description="A senha deve ter de 12 a 128 caracteres."}));
}
