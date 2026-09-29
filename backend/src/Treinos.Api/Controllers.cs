using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Treinos.Application;

namespace Treinos.Api;

[ApiController, Route("api/auth")]
public sealed class AuthController(AccountService accounts, PasswordRecovery recovery, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    public object Csrf() { var tokens = antiforgery.GetAndStoreTokens(HttpContext); Response.Headers.CacheControl = "no-store"; return new { token = tokens.RequestToken }; }
    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await accounts.FindById(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return user is null ? Unauthorized() : Ok(new Account(user.Id, user.DisplayName, user.Email, user.AppRole, user.MustChangePassword, user.TimeZone));
    }
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await accounts.Login(request.Email, request.Password);
        if (user is null) return Unauthorized(new { code = "INVALID_CREDENTIALS" });
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Role, user.AppRole), new Claim("security_stamp", user.SecurityStamp) };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return Ok(new Account(user.Id, user.DisplayName, user.Email, user.AppRole, user.MustChangePassword, user.TimeZone));
    }
    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return NoContent(); }
    [HttpPost("change-password"), Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var changed = await accounts.ChangePassword(User.FindFirstValue(ClaimTypes.NameIdentifier)!, request.OldPassword, request.NewPassword);
        if (!changed) return BadRequest(new { code = "PASSWORD_CHANGE_FAILED" });
        await HttpContext.SignOutAsync(); return NoContent();
    }
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        if (!recovery.Enabled) return StatusCode(503, new { code = "EMAIL_NOT_CONFIGURED" });
        await recovery.Request(request.Email); return Accepted(new { message = "Se a conta existir, enviaremos instruções." });
    }
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request) => await recovery.Reset(request.Id, request.Token, request.NewPassword) ? NoContent() : BadRequest(new { code = "RESET_INVALID" });
}
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Id, string Token, string NewPassword);
public record LoginRequest(string Email, string Password);
public record ChangePasswordRequest(string OldPassword, string NewPassword);
public record NewUserRequest(string Name, string Email, string InitialPassword);

[ApiController, Route("api/admin/users"), Authorize(Policy = "CanCreateUsers")]
public sealed class AdminController(AccountService accounts) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(NewUserRequest request)
    {
        try { return Ok(await accounts.Create(request.Name, request.Email, request.InitialPassword, "common", true)); }
        catch (ArgumentException e) { return BadRequest(new { code = "INVALID_USER", detail = e.Message }); }
    }
}
[ApiController, Route("api/exercises"), Authorize]
public sealed class ExercisesController(ITrainingService service) : ControllerBase
{
    [HttpGet] public async Task<object> List(CancellationToken ct) => await service.Exercises(ct);
    [HttpGet("{id}")] public async Task<IActionResult> Get(string id, CancellationToken ct) => await service.Exercise(id, ct) is { } item ? Ok(item) : NotFound();
    [HttpPost] public async Task<IActionResult> Create(ExerciseInput input, CancellationToken ct) { try { var item = await service.SaveExercise(null, input, ct); return Created($"/api/exercises/{item.Id}", item); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpPut("{id}")] public async Task<IActionResult> Update(string id, ExerciseInput input, CancellationToken ct) { try { return Ok(await service.SaveExercise(id, input, ct)); } catch (InvalidOperationException) { return Conflict(); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpDelete("{id}")] public async Task<IActionResult> Delete(string id, [FromQuery] long expectedVersion, CancellationToken ct) => await service.DeleteExercise(id, expectedVersion, ct) ? NoContent() : Conflict();
}
[ApiController, Route("api/plans"), Authorize]
public sealed class PlansController(ITrainingService service) : ControllerBase
{
    [HttpGet] public async Task<object> List(CancellationToken ct) => await service.Plans(ct);
    [HttpPost] public async Task<IActionResult> Create(PlanInput input, CancellationToken ct) { try { var id = await service.SavePlan(null, input, ct); return Created($"/api/plans/{id}", new { id }); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpPut("{id}")] public async Task<IActionResult> Update(string id, PlanInput input, CancellationToken ct) { try { await service.SavePlan(id, input, ct); return NoContent(); } catch (InvalidOperationException) { return Conflict(); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpPost("{id}/templates")] public async Task<IActionResult> CreateTemplate(string id, TemplateInput input, CancellationToken ct) { try { var templateId = await service.SaveTemplate(null, id, input, ct); return Created($"/api/templates/{templateId}", new { id = templateId }); } catch (ArgumentException e) { return BadRequest(e.Message); } }
}
[ApiController, Route("api/templates"), Authorize]
public sealed class TemplatesController(ITrainingService service) : ControllerBase
{
    [HttpPut("{id}")] public async Task<IActionResult> Update(string id, [FromQuery] string planId, TemplateInput input, CancellationToken ct) { try { await service.SaveTemplate(id, planId, input, ct); return NoContent(); } catch (InvalidOperationException) { return Conflict(); } catch (ArgumentException e) { return BadRequest(e.Message); } }
}
[ApiController, Route("api/sessions"), Authorize]
public sealed class SessionsController(ITrainingService service) : ControllerBase
{
    [HttpGet] public async Task<object> List(CancellationToken ct) => await service.Sessions(ct);
}
[ApiController, Route("api/sync"), Authorize]
public sealed class SyncController(ITrainingService service) : ControllerBase
{
    [HttpGet("bootstrap")] public async Task<object> Bootstrap(CancellationToken ct) => await service.Bootstrap(ct);
    [HttpGet("changes")] public async Task<IActionResult> Changes([FromQuery] long after, [FromQuery] int limit, CancellationToken ct) { try { return Ok(await service.Changes(after, limit, ct)); } catch (InvalidOperationException) { return StatusCode(410, new { code = "SYNC_CURSOR_EXPIRED" }); } }
    [HttpPost("operations")] public async Task<IActionResult> Apply(SyncOperation operation, CancellationToken ct) { try { return Ok(await service.Apply(operation, ct)); } catch (InvalidOperationException e) { return Conflict(new { code = e.Message }); } catch (ArgumentException e) { return BadRequest(new { code = "VALIDATION", detail = e.Message }); } }
}
[ApiController, Route("api/reports"), Authorize]
public sealed class ReportsController(ITrainingService service) : ControllerBase
{
    [HttpGet("frequency")] public async Task<IActionResult> Frequency([FromQuery] string from, [FromQuery] string to, CancellationToken ct) { try { return Ok(await service.Frequency(from, to, ct)); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpGet("exercises/{id}")] public async Task<IActionResult> Exercise(string id, [FromQuery] string from, [FromQuery] string to, CancellationToken ct) { try { return Ok(await service.ExerciseProgress(id, from, to, ct)); } catch (ArgumentException e) { return BadRequest(e.Message); } }
}
