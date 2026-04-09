using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtterApi.Builders;
using OtterApi.Configs;
using OtterApi.Controllers;
using OtterApi.Exceptions;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Processors;

public class OtterApiRequestProcessor(
    IServiceProvider serviceProvider,
    IObjectModelValidator objectModelValidator,
    IOtterApiRegistry registry)
    : IOtterApiRequestProcessor
{
    public async Task<object> GetData(HttpRequest request, Type type)
    {
        var result = await JsonSerializer.DeserializeAsync(request.Body, type, registry.DeserializationOptions);
        if (result is null)
            throw new OtterApiException(
                "INVALID_BODY",
                "Request body must not be null or empty.",
                StatusCodes.Status400BadRequest);
        return result;
    }

    public async Task<JsonObject> GetPatchData(HttpRequest request)
    {
        return await JsonSerializer.DeserializeAsync<JsonObject>(request.Body)
               ?? new JsonObject();
    }


    public OtterApiRouteInfo GetRouteInfo(HttpRequest request)
    {
        var result = new OtterApiRouteInfo();

        // O(1) lookup: at most two dictionary probes replace the previous O(n) linear scan.
        var apiEntity = registry.FindEntityForPath(request.Path, out var path);

        result.Entity = apiEntity;

        if (path.HasValue)
        {
            var value = path.Value.TrimStart('/');
            switch (value)
            {
                case "count":
                    result.IsCount = true;
                    break;

                case "pagedresult":
                    result.IsPageResult = apiEntity.ExposePagedResult;
                    break;

                default:
                    if (!string.IsNullOrEmpty(value))
                    {
                        // Check named custom routes before falling back to treating the segment as an Id
                        var customRoute = apiEntity.CustomRoutes
                            .FirstOrDefault(r => r.Slug.Equals(value, StringComparison.OrdinalIgnoreCase));

                        if (customRoute != null)
                            result.CustomRoute = customRoute;
                        else
                            result.Id = value;
                    }
                    break;
            }
        }

        if (apiEntity != null && string.IsNullOrWhiteSpace(result.Id) && request.Query?.Keys.Count > 0)
        {
            // Pass DeserializationOptions so that in/nin correctly handles enum string names
            // via OtterApiCaseInsensitiveEnumConverterFactory (same converter used for POST/PUT bodies).
            var expressionBuilder = new OtterApiExpressionBuilder(request.Query, apiEntity, registry.DeserializationOptions);

            result.FilterApply = expressionBuilder.BuildFilterResult();
            result.SortApply   = expressionBuilder.BuildSortResult();

            var pageResult = expressionBuilder.BuildPagingResult();
            result.Take = pageResult.Take;
            result.Skip = pageResult.Skip;
            result.Page = pageResult.Page;

            result.IncludeExpression = expressionBuilder.BuildIncludeResult();
        }

        return result;

    }

    public IOtterApiRestController GetController(ActionContext actionContext, Type dbContextType)
    {
        var dbContext = (DbContext)serviceProvider.GetRequiredService(dbContextType);
        return new OtterApiRestController(dbContext, actionContext, objectModelValidator, serviceProvider, registry);
    }

    public IActionResultExecutor<ObjectResult> GetActionExecutor()
    {
        return serviceProvider.GetRequiredService<IActionResultExecutor<ObjectResult>>();
    }
}