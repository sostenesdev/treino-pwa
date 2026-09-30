using System.Security.Claims;
using Treinos.Application;

namespace Treinos.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAdministrator => accessor.HttpContext?.User.IsInRole("administrator") == true;
    public string Id => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
}

public sealed class HttpTrainingOwner(IHttpContextAccessor accessor, ICurrentUser current) : ITrainingOwner
{
    public string OwnerId => accessor.HttpContext?.Items["training_owner"] as string ?? current.Id;
}
