using Microsoft.AspNetCore.DataProtection;

using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Treinos.Api;
using Treinos.Application;
using Treinos.Infrastructure;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);
DefaultTypeMap.MatchNamesWithUnderscores = true;
var connection = builder.Configuration.GetConnectionString("Treinos") ?? throw new InvalidOperationException("ConnectionStrings:Treinos ausente");
var passwordFile = builder.Configuration["Database:PasswordFile"];
if (!string.IsNullOrEmpty(passwordFile))
{
    var settings = new MySqlConnectionStringBuilder(connection) { Password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n') };
    connection = settings.ConnectionString;
}
builder.Services.AddSingleton(new Database(connection));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ITrainingService, TrainingService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<PasswordRecovery>();
builder.Services.AddHostedService<EmailDispatcher>();
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeysPath"] ?? "/var/lib/treinos/keys"));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "__Host-treinos"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.Path = "/";
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async c =>
    {
        var id = c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = c.Principal?.FindFirstValue("security_stamp");
        if (id is null || stamp is null) { c.RejectPrincipal(); return; }
        var account = await c.HttpContext.RequestServices.GetRequiredService<AccountService>().FindById(id);
        if (account is null || account.SecurityStamp != stamp) { c.RejectPrincipal(); return; }
        if (account.MustChangePassword && c.HttpContext.Request.Path.StartsWithSegments("/api") && !c.HttpContext.Request.Path.StartsWithSegments("/api/auth")) c.HttpContext.Items["password_change_required"] = true;
    };
});
builder.Services.AddAuthorization(o => o.AddPolicy("CanCreateUsers", p => p.RequireRole("administrator")));
var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Items.ContainsKey("password_change_required")) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { code = "PASSWORD_CHANGE_REQUIRED" }); return; }
    if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { code = "CSRF_INVALID" }); return; }
    }
    await next();
});
app.MapGet("/api/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/health/ready", async (Database database) => { await using var db = await database.Open(); await db.ExecuteScalarAsync<int>("SELECT 1"); return Results.Ok(new { status = "ok" }); });
app.MapControllers();
if (args.Contains("bootstrap-admin"))
{
    var email = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_EMAIL") ?? throw new InvalidOperationException("BOOTSTRAP_ADMIN_EMAIL ausente");
    var name = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_NAME") ?? throw new InvalidOperationException("BOOTSTRAP_ADMIN_NAME ausente");
    var adminPasswordFile = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD_FILE") ?? throw new InvalidOperationException("BOOTSTRAP_ADMIN_PASSWORD_FILE ausente");
    using var scope = app.Services.CreateScope();
    var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
    var existing = await accounts.FindByEmail(email);
    if (existing is null) { var created = await accounts.Create(name, email, (await File.ReadAllTextAsync(adminPasswordFile)).TrimEnd('\r','\n'), "administrator", false); Console.WriteLine($"Administrador criado: {created.Id}"); }
    else if (existing.AppRole == "administrator") Console.WriteLine($"Administrador já existente: {existing.Id}");
    else throw new InvalidOperationException("E-mail pertence a uma conta comum.");
    return;
}
app.Run();
