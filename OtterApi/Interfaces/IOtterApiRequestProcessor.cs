using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using OtterApi.Models;

namespace OtterApi.Interfaces;

public interface IOtterApiRequestProcessor
{
    OtterApiRouteInfo GetRoutInfo(HttpRequest request);

    Task<object> GetData(HttpRequest request, Type type);

    Task<JsonObject> GetPatchData(HttpRequest request);

    IOtterApiRestController GetController(ActionContext actionContext, Type dbContextType);

    IActionResultExecutor<ObjectResult> GetActionExecutor();
}