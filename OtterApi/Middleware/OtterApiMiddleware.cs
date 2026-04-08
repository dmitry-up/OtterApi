using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using OtterApi.Enums;
using OtterApi.Exceptions;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Middleware;

public class OtterApiMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOtterApiRequestProcessor otterApiRequestProcessor,
        IAuthorizationService authorizationService)
    {
        var routeInfo = otterApiRequestProcessor.GetRoutInfo(context.Request);
        var result = new ObjectResult(null);

        if (routeInfo.Entity != null)
        {
            if (!await IsAuthorizedAsync(authorizationService, context.User, routeInfo.Entity, context.Request.Method))
            {
                context.Response.StatusCode = context.User.Identity?.IsAuthenticated == true ? 403 : 401;
                return;
            }

            var executor = otterApiRequestProcessor.GetActionExecutor();
            var actionContext = new ActionContext(context, new RouteData(), new ActionDescriptor());
            var controller = otterApiRequestProcessor.GetController(actionContext, routeInfo.Entity.DbContextType);

            var requestedOperation = context.Request.Method switch
            {
                "GET"    => OtterApiCrudOperation.Get,
                "POST"   => OtterApiCrudOperation.Post,
                "PUT"    => OtterApiCrudOperation.Put,
                "DELETE" => OtterApiCrudOperation.Delete,
                "PATCH"  => OtterApiCrudOperation.Patch,
                _        => (OtterApiCrudOperation?)null
            };

            if (requestedOperation == null || (routeInfo.Entity.AllowedOperations & requestedOperation.Value) == 0)
            {
                context.Response.StatusCode = 405;
                return;
            }

            try
            {
                switch (context.Request.Method)
                {
                    case "GET":
                        result = await controller.GetAsync(routeInfo, context.RequestAborted);
                        break;

                    case "POST":
                        result = await controller.PostAsync(routeInfo,
                            await otterApiRequestProcessor.GetData(context.Request, routeInfo.Entity.EntityType),
                            context.RequestAborted);
                        break;

                    case "PUT":
                        result = await controller.PutAsync(routeInfo,
                            await otterApiRequestProcessor.GetData(context.Request, routeInfo.Entity.EntityType),
                            context.RequestAborted);
                        break;

                    case "DELETE":
                        result = await controller.DeleteAsync(routeInfo, context.RequestAborted);
                        break;

                    case "PATCH":
                        result = await controller.PatchAsync(routeInfo,
                            await otterApiRequestProcessor.GetPatchData(context.Request),
                            context.RequestAborted);
                        break;
                }
            }
            catch (OtterApiException ex)
            {
                context.Response.StatusCode = ex.StatusCode;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    code = ex.Code,
                    message = ex.Message
                }));
                return;
            }

            await executor.ExecuteAsync(actionContext, result);
        }
        else
        {
            await next(context);
        }
    }

    public async Task<bool> IsAuthorizedAsync(IAuthorizationService authorizationService, ClaimsPrincipal claimsPrincipal,
        OtterApiEntity aPiEntity, string method)
    {
        if (claimsPrincipal.Identity?.IsAuthenticated != true && aPiEntity.Authorize)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(aPiEntity.EntityPolicy)
            && !await AuthorizeAsync(authorizationService, claimsPrincipal, aPiEntity.EntityPolicy))
        {
            return false;
        }

        var methodPolicy = "";

        switch (method)
        {
            case "GET":
                methodPolicy = aPiEntity.GetPolicy;
                break;

            case "POST":
                methodPolicy = aPiEntity.PostPolicy;
                break;

            case "PUT":
                methodPolicy = aPiEntity.PutPolicy;
                break;

            case "DELETE":
                methodPolicy = aPiEntity.DeletePolicy;
                break;

            case "PATCH":
                methodPolicy = aPiEntity.PatchPolicy;
                break;
        }

        if (!string.IsNullOrWhiteSpace(methodPolicy) && !await AuthorizeAsync(authorizationService, claimsPrincipal, methodPolicy))
        {
            return false;
        }

        return true;
    }

    protected virtual async Task<bool> AuthorizeAsync(IAuthorizationService authorizationService,
        ClaimsPrincipal claimsPrincipal, string policy)
    {
        return (await authorizationService.AuthorizeAsync(claimsPrincipal, policy)).Succeeded;
    }
}