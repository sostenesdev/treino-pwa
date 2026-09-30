using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Treinos.Application;

namespace Treinos.Api;

[ApiController, Route("api/auth")]
public sealed class AuthController(IAccountService accounts, IPasswordRecovery recovery, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    public object Csrf() { var tokens = antiforgery.GetAndStoreTokens(HttpContext); Response.Headers.CacheControl = "no-store"; return new { token = tokens.RequestToken }; }
    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await accounts.GetCurrent();
        return user is null ? Unauthorized() : Ok(user);
    }
    [HttpPost("login"), Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await accounts.Login(request.Email, request.Password);
        if (user is null) return Unauthorized(new { code = "INVALID_CREDENTIALS" });
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, user.Principal);
        return Ok(user.Account);
    }
    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return NoContent(); }
    [HttpPost("change-password"), Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var changed = await accounts.ChangePassword(request.OldPassword, request.NewPassword);
        if (!changed) return BadRequest(new { code = "PASSWORD_CHANGE_FAILED" });
        await HttpContext.SignOutAsync(); return NoContent();
    }
    [HttpPost("forgot-password"), Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        if (!recovery.Enabled) return StatusCode(503, new { code = "EMAIL_NOT_CONFIGURED" });
        await recovery.Request(request.Email); return Accepted(new { message = "Se a conta existir, enviaremos instruções." });
    }
    [HttpPost("reset-password"), Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request) => await recovery.Reset(request.Id, request.Token, request.NewPassword) ? NoContent() : BadRequest(new { code = "RESET_INVALID" });
}
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Id, string Token, string NewPassword);
public record LoginRequest(string Email, string Password);
public record ChangePasswordRequest(string OldPassword, string NewPassword);
public record NewUserRequest(string Name, string Email, string InitialPassword);

[ApiController, Route("api/admin/users"), Authorize(Policy = "CanCreateUsers")]
public sealed class AdminController(IAccountService accounts) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(NewUserRequest request)
    {
        try { return Ok(await accounts.CreateCommonUser(request.Name, request.Email, request.InitialPassword)); }
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
    [HttpDelete("{id}")] public async Task<IActionResult> Delete(string id, [FromQuery] long expectedVersion, CancellationToken ct) => Ok(await service.DeleteExercise(id, expectedVersion, ct));
}
[ApiController, Route("api/plans"), Authorize]
public sealed class PlansController(ITrainingService service) : ControllerBase
{
    [HttpGet] public async Task<object> List(CancellationToken ct) => await service.Plans(ct);
    [HttpGet("{id}")] public async Task<IActionResult> Get(string id,CancellationToken ct)=> (await service.Plans(ct)).Find(p=>p.Id==id) is {} plan?Ok(plan):NotFound();
    [HttpDelete("{id}")] public async Task<IActionResult> Delete(string id,[FromQuery] long expectedVersion,CancellationToken ct)=>Ok(new{version=await service.ArchiveDefinition("plan",id,expectedVersion,ct)});
    [HttpPost] public async Task<IActionResult> Create(PlanInput input, CancellationToken ct) { try { var id = await service.SavePlan(null, input, ct); return Created($"/api/plans/{id}", new { id }); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpPut("{id}")] public async Task<IActionResult> Update(string id, PlanInput input, CancellationToken ct) { try { await service.SavePlan(id, input, ct); return NoContent(); } catch (InvalidOperationException) { return Conflict(); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpPost("{id}/templates")] public async Task<IActionResult> CreateTemplate(string id, TemplateInput input, CancellationToken ct) { try { var templateId = await service.SaveTemplate(null, id, input, ct); return Created($"/api/templates/{templateId}", new { id = templateId }); } catch (ArgumentException e) { return BadRequest(e.Message); } }
}
[ApiController, Route("api/templates"), Authorize]
public sealed class TemplatesController(ITrainingService service) : ControllerBase
{
    [HttpGet("{id}")] public async Task<IActionResult> Get(string id,CancellationToken ct)=>(await service.Plans(ct)).SelectMany(p=>p.Templates).FirstOrDefault(t=>t.Id==id) is {} template?Ok(template):NotFound();
    [HttpDelete("{id}")] public async Task<IActionResult> Delete(string id,[FromQuery] long expectedVersion,CancellationToken ct)=>Ok(new{version=await service.ArchiveDefinition("template",id,expectedVersion,ct)});
    [HttpPut("{id}")] public async Task<IActionResult> Update(string id, [FromQuery] string planId, TemplateInput input, CancellationToken ct) { try { await service.SaveTemplate(id, planId, input, ct); return NoContent(); } catch (InvalidOperationException) { return Conflict(); } catch (ArgumentException e) { return BadRequest(e.Message); } }
}
[ApiController, Route("api/sessions"), Authorize]
public sealed class SessionsController(ITrainingService service, SessionCommands commands) : ControllerBase
{
    [HttpGet] public async Task<object> List(CancellationToken ct, [FromQuery] string? from = null, [FromQuery] string? to = null, [FromQuery] int page = 1) => await service.Sessions(ct,from,to,page);
    [HttpGet("{id}")] public async Task<IActionResult> Get(string id,CancellationToken ct) => await service.Session(id,ct) is {Deleted:false} item?Ok(item):NotFound();
    [HttpPost] public Task<SyncAck> Create(SessionCommand command,CancellationToken ct) => commands.Execute("create",command.Snapshot?.Id ?? "",command,null,null,ct);
    [HttpPut("{id}")] public Task<SyncAck> Update(string id,SessionCommand command,CancellationToken ct)=>commands.Execute("replace",id,command,null,null,ct);
    [HttpPost("{id}/exercises")] public Task<SyncAck> AddExercise(string id,SessionCommand command,CancellationToken ct)=>commands.Execute("replace",id,command,null,null,ct);
    [HttpPut("{id}/exercises/{itemId}")] public Task<SyncAck> UpdateExercise(string id,string itemId,SessionCommand command,CancellationToken ct)=>commands.Execute("exercise",id,command,itemId,null,ct);
    [HttpDelete("{id}/exercises/{itemId}")] public Task<SyncAck> RemoveExercise(string id,string itemId,[FromBody] SessionCommand command,CancellationToken ct)=>commands.Execute("remove-exercise",id,command,itemId,null,ct);
    [HttpPut("{id}/exercises/{itemId}/sets/{setId}")] public Task<SyncAck> SaveSet(string id,string itemId,string setId,SessionCommand command,CancellationToken ct)=>commands.Execute("set",id,command,itemId,setId,ct);
    [HttpDelete("{id}/exercises/{itemId}/sets/{setId}")] public Task<SyncAck> RemoveSet(string id,string itemId,string setId,[FromBody] SessionCommand command,CancellationToken ct)=>commands.Execute("remove-set",id,command,itemId,setId,ct);
    [HttpPost("{id}/complete")] public Task<SyncAck> Complete(string id,SessionCommand command,CancellationToken ct)=>commands.Execute("complete",id,command,null,null,ct);
    [HttpDelete("{id}")] public Task<SyncAck> Delete(string id,[FromBody] SessionCommand command,[FromQuery] long expectedVersion,CancellationToken ct)
    {
        if(command.BaseVersion!=expectedVersion)throw new ArgumentException("Versão divergente.");
        return commands.Execute("delete",id,command,null,null,ct);
    }
}
[ApiController, Route("api/sync"), Authorize]
public sealed class SyncController(ITrainingService service) : ControllerBase
{
    [HttpGet("bootstrap")] public async Task<object> Bootstrap(CancellationToken ct) => await service.Bootstrap(ct);
    [HttpGet("changes")] public async Task<IActionResult> Changes([FromQuery] long after, [FromQuery] int limit = 100, CancellationToken ct = default) { try { return Ok(await service.Changes(after, limit, ct)); } catch (InvalidOperationException) { return StatusCode(410, new { code = "SYNC_CURSOR_EXPIRED" }); } }
    [HttpPost("operations")] public async Task<IActionResult> Apply(SyncOperation operation, CancellationToken ct) { try { return Ok(await service.Apply(operation, ct)); } catch (InvalidOperationException e) { return Conflict(new { code = e.Message }); } catch (ArgumentException e) { return BadRequest(new { code = "VALIDATION", detail = e.Message }); } }
    [HttpPost("push")] public async Task<IActionResult> Push(List<SyncOperation> operations,CancellationToken ct)
    {
        if(operations.Count is <1 or >20)return BadRequest(new{code="BATCH_LIMIT"});
        var results=new List<object>();
        foreach(var operation in operations)
        {
            try{results.Add(new{operation.OperationId,status=200,result=await service.Apply(operation,ct)});}
            catch(SyncConflictException e){results.Add(new{operation.OperationId,status=409,code="VERSION_CONFLICT",e.Version,e.Snapshot,e.Deleted});}
            catch(ResourceNotFoundException){results.Add(new{operation.OperationId,status=404,code="NOT_FOUND"});}
            catch(ArgumentException e){results.Add(new{operation.OperationId,status=400,code="VALIDATION",detail=e.Message});}
            catch(InvalidOperationException e){results.Add(new{operation.OperationId,status=409,code=e.Message});}
        }
        return Ok(new{results});
    }
}
[ApiController, Route("api/reports"), Authorize]
public sealed class ReportsController(ITrainingService service) : ControllerBase
{
    [HttpGet("frequency")] public async Task<IActionResult> Frequency([FromQuery] string from, [FromQuery] string to, CancellationToken ct) { try { return Ok(await service.Frequency(from, to, ct)); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpGet("exercises/{id}")] public async Task<IActionResult> Exercise(string id, [FromQuery] string from, [FromQuery] string to, CancellationToken ct) { try { return Ok(await service.ExerciseProgress(id, from, to, ct)); } catch (ArgumentException e) { return BadRequest(e.Message); } }
}
