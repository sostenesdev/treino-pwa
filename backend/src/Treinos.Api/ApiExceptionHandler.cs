using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Treinos.Application;

namespace Treinos.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context,Exception exception,CancellationToken ct)
    {
        var status=exception switch { ResourceNotFoundException=>404,SyncConflictException=>409,ArgumentException=>400,UnauthorizedAccessException=>403,InvalidOperationException e when e.Message=="SYNC_CURSOR_EXPIRED"=>410,InvalidOperationException=>409,_=>500 };
        var detail=new ProblemDetails{Status=status,Title=status==500?"Falha ao processar a solicitação.":exception.Message};
        if(status!=500)detail.Extensions["code"]=exception is SyncConflictException?"VERSION_CONFLICT":exception is ResourceNotFoundException?"NOT_FOUND":exception is ArgumentException?"VALIDATION":exception.Message;
        if(exception is SyncConflictException conflict){detail.Extensions["version"]=conflict.Version;detail.Extensions["snapshot"]=conflict.Snapshot;detail.Extensions["deleted"]=conflict.Deleted;}
        context.Response.StatusCode=status;
        return await problems.TryWriteAsync(new ProblemDetailsContext{HttpContext=context,ProblemDetails=detail,Exception=exception});
    }
}
