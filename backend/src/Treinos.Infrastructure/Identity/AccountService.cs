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
    private async Task<Account> Create(string name,string email,string password,string role,bool change,CancellationToken ct,string timeZone="America/Sao_Paulo")
    {
        ct.ThrowIfCancellationRequested();if(string.IsNullOrWhiteSpace(name)||name.Trim().Length is <2 or >120)throw new ArgumentException("Nome inválido.");
        var user=new AppUser{Id=Guid.NewGuid().ToString(),UserName=email.Trim(),Email=email.Trim(),DisplayName=name.Trim(),AppRole=role,MustChangePassword=change,TimeZone=timeZone};
        Ensure(await users.CreateAsync(user,password));return View(user);
    }
    private async Task<AppUser> Administrator()
    {
        var actor=await users.FindByIdAsync(current.Id);
        if(actor is null || actor.AppRole!="administrator" || actor.MustChangePassword || await users.IsLockedOutAsync(actor))throw new UnauthorizedAccessException("Acesso restrito ao administrador.");
        return actor;
    }
    public async Task<List<UserDto>> Users(CancellationToken ct=default)
    {
        await Administrator(); await using var db=await database.Open(ct);
        return (await db.QueryAsync<AppUser>("SELECT id,display_name,email,app_role,must_change_password,time_zone,row_version FROM app_users WHERE deleted_at_utc IS NULL ORDER BY display_name,email")).Select(UserView).ToList();
    }
    public async Task<UserDto?> User(string id,CancellationToken ct=default)
    {
        if(id!=current.Id)await Administrator();
        var user=await users.FindByIdAsync(id);return user is null?null:UserView(user);
    }
    private static void ValidateUser(UserInput input)
    {
        if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Trim().Length is <2 or >120||string.IsNullOrWhiteSpace(input.Email)||input.Email.Length>256||input.Role is not ("administrator" or "common"))throw new ArgumentException("Nome, e-mail ou perfil inválidos.");
        try{TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone);}catch(Exception e) when(e is TimeZoneNotFoundException or InvalidTimeZoneException){throw new ArgumentException("Fuso horário inválido.");}
    }
    public async Task<UserDto> CreateUser(UserInput input,CancellationToken ct=default)
    {
        await Administrator();ValidateUser(input);
        if(input.InitialPassword is null)throw new ArgumentException("Informe a senha inicial.");
        var account=await Create(input.Name,input.Email,input.InitialPassword,input.Role,true,ct,input.TimeZone);
        return UserView((await users.FindByIdAsync(account.Id))!);
    }
    private async Task ProtectLastAdministrator(MySqlConnector.MySqlConnection db,AppUser user,bool removingAdmin)
    {
        if(removingAdmin && user.AppRole=="administrator" && await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM app_users WHERE app_role='administrator' AND deleted_at_utc IS NULL")<=1)throw new ArgumentException("Mantenha pelo menos um administrador ativo.");
    }
    public async Task<UserDto> UpdateUser(string id,UserInput input,CancellationToken ct=default)
    {
        ValidateUser(input);var actor=await users.FindByIdAsync(current.Id)??throw new UnauthorizedAccessException();
        if(id!=actor.Id || input.Role!=actor.AppRole)await Administrator();
        await using var db=await database.Open(ct);
        if(await db.ExecuteScalarAsync<int>("SELECT GET_LOCK('treinos_user_management',30)")!=1)throw new InvalidOperationException("Cadastro de usuários em uso.");
        try {
            var user=await users.FindByIdAsync(id)??throw new ResourceNotFoundException();
            if(input.ExpectedVersion<1 || user.RowVersion!=input.ExpectedVersion)throw new InvalidOperationException("VERSION_CONFLICT");
            await ProtectLastAdministrator(db,user,input.Role!="administrator");
            user.DisplayName=input.Name.Trim();user.Email=input.Email.Trim();user.UserName=user.Email;user.AppRole=input.Role;user.TimeZone=input.TimeZone;user.SecurityStamp=Guid.NewGuid().ToString();
            Ensure(await users.UpdateAsync(user));return UserView(user);
        } finally { await db.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK('treinos_user_management')"); }
    }
    public async Task DeleteUser(string id,long expectedVersion,CancellationToken ct=default)
    {
        await Administrator();await using var db=await database.Open(ct);
        if(await db.ExecuteScalarAsync<int>("SELECT GET_LOCK('treinos_user_management',30)")!=1)throw new InvalidOperationException("Cadastro de usuários em uso.");
        try {
            var user=await users.FindByIdAsync(id)??throw new ResourceNotFoundException();
            if(user.RowVersion!=expectedVersion)throw new InvalidOperationException("VERSION_CONFLICT");
            await ProtectLastAdministrator(db,user,true);Ensure(await users.DeleteAsync(user));
        } finally { await db.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK('treinos_user_management')"); }
    }
    private static UserDto UserView(AppUser user)=>new(user.Id,user.DisplayName,user.Email!,user.AppRole,user.MustChangePassword,user.TimeZone,user.RowVersion);
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
    private static void Ensure(IdentityResult result){if(result.Errors.Any(e=>e.Code=="ConcurrencyFailure"))throw new InvalidOperationException("VERSION_CONFLICT");if(!result.Succeeded)throw new ArgumentException(string.Join(" ",result.Errors.Select(e=>e.Description)));}
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
