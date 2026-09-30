using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Treinos.Api;
using Treinos.Application;
using Treinos.Infrastructure;
using MySqlConnector;
using Treinos.Infrastructure.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1048576);
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
builder.Services.AddScoped<SessionCommands>();
builder.Services.Configure<ForwardedHeadersOptions>(o => {
    o.ForwardedHeaders=ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownProxies.Add(IPAddress.Loopback); o.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.Password.RequiredLength = 12; o.Password.RequireDigit = false; o.Password.RequireLowercase = false;
    o.Password.RequireUppercase = false; o.Password.RequireNonAlphanumeric = false; o.User.RequireUniqueEmail = true;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); o.Lockout.MaxFailedAccessAttempts = 5;
}).AddUserStore<DapperUserStore>().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddScoped<IPasswordValidator<AppUser>, PasswordLengthValidator>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, PrincipalFactory>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<IAccountService>(s => s.GetRequiredService<AccountService>());
builder.Services.AddScoped<PasswordRecovery>();
builder.Services.AddScoped<IPasswordRecovery>(s => s.GetRequiredService<PasswordRecovery>());
builder.Services.AddHostedService<EmailDispatcher>();
builder.Services.AddHostedService<RetentionMaintenance>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (context, ct) => {
        context.HttpContext.Response.Headers.RetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter,out var retry) ? Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) : "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new {code="RATE_LIMITED"},ct);
    };
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
var requireHttpsCookies=builder.Configuration.GetValue<bool>("Auth:RequireHttpsCookies",true);
if(!requireHttpsCookies && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Cookies HTTP são permitidos somente no ambiente Development.");
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name=requireHttpsCookies?"__Host-treinos-csrf":"treinos-csrf"; o.Cookie.Path="/"; o.Cookie.SecurePolicy=requireHttpsCookies?CookieSecurePolicy.Always:CookieSecurePolicy.SameAsRequest; o.Cookie.SameSite=SameSiteMode.Lax; });
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeysPath"] ?? "/var/lib/treinos/keys"));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = requireHttpsCookies?"__Host-treinos":"treinos"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = requireHttpsCookies?CookieSecurePolicy.Always:CookieSecurePolicy.SameAsRequest; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.Path = "/";
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async c =>
    {
        var id = c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = c.Principal?.FindFirstValue("security_stamp");
        if (id is null || stamp is null) { c.RejectPrincipal(); return; }
        var account = await c.HttpContext.RequestServices.GetRequiredService<IAccountService>().ValidateSession(id, stamp, c.Principal!.FindFirstValue(ClaimTypes.Role) ?? "");
        if (account is null) { c.RejectPrincipal(); return; }
        if (account.MustChangePassword && c.HttpContext.Request.Path.StartsWithSegments("/api") && !new[] {"/api/auth/me", "/api/auth/csrf", "/api/auth/logout", "/api/auth/change-password"}.Contains(c.HttpContext.Request.Path.Value)) c.HttpContext.Items["password_change_required"] = true;
    };
});
builder.Services.AddAuthorization(o => o.AddPolicy("CanCreateUsers", p => p.RequireRole("administrator")));
var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:RequireMigrations"))
{
    await using var db = await app.Services.GetRequiredService<Database>().Open();
    if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version IN ('001_schema_treinos.sql','003_pwa_usuarios_email.sql')") != 2)
        throw new InvalidOperationException("Aplique as migrações antes de iniciar a API.");
}
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if(context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl="no-store";
    if (context.Items.ContainsKey("password_change_required")) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { code = "PASSWORD_CHANGE_REQUIRED" }); return; }
    if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { code = "CSRF_INVALID" }); return; }
    }
    await next();
});
app.MapGet("/api/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/health/connectivity", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/health/ready", async (Database database) => { await using var db = await database.Open(); if(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version IN ('001_schema_treinos.sql','003_pwa_usuarios_email.sql')") != 2) return Results.StatusCode(503); return Results.Ok(new { status = "ok" }); });
app.MapControllers();
if (args.Contains("bootstrap-admin"))
{
    var email = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_EMAIL") ?? throw new InvalidOperationException("BOOTSTRAP_ADMIN_EMAIL ausente");
    var name = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_NAME") ?? throw new InvalidOperationException("BOOTSTRAP_ADMIN_NAME ausente");
    var adminPasswordFile = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD_FILE") ?? throw new InvalidOperationException("BOOTSTRAP_ADMIN_PASSWORD_FILE ausente");
    using var scope = app.Services.CreateScope();
    var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
    var account = await accounts.BootstrapAdministrator(name, email, (await File.ReadAllTextAsync(adminPasswordFile)).TrimEnd('\r','\n'), Environment.GetEnvironmentVariable("BOOTSTRAP_PROMOTE_USER_ID"));
    Console.WriteLine($"Administrador provisionado: {account.Id}");
    return;
}
app.Run();
