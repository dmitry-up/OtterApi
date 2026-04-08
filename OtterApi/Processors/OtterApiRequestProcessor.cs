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
using OtterApi.Converters;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Processors;

public class OtterApiRequestProcessor(
    IServiceProvider serviceProvider,
    IObjectModelValidator objectModelValidator,
    OtterApiRegistry registry)
    : IOtterApiRequestProcessor
{
    public async Task<object> GetData(HttpRequest request, Type type)
    {
        var baseOptions = registry.Options.JsonSerializerOptions;
        JsonSerializerOptions options;

        if (baseOptions == null)
        {
            options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new OtterApiCaseInsensitiveEnumConverterFactory());
        }
        else
        {
            options = baseOptions.PropertyNameCaseInsensitive
                ? new JsonSerializerOptions(baseOptions)
                : new JsonSerializerOptions(baseOptions)
                {
                    PropertyNameCaseInsensitive = true
                };

            if (!options.Converters.Any(c => c is OtterApiCaseInsensitiveEnumConverterFactory))
                options.Converters.Add(new OtterApiCaseInsensitiveEnumConverterFactory());
        }

        return await JsonSerializer.DeserializeAsync(request.Body, type, options);
    }

    public async Task<JsonObject> GetPatchData(HttpRequest request)
    {
        return await JsonSerializer.DeserializeAsync<JsonObject>(request.Body)
               ?? new JsonObject();
    }


    public OtterApiRouteInfo GetRoutInfo(HttpRequest request)
    {
        PathString path = null;
        var result = new OtterApiRouteInfo();

        var apiEntity = registry.Entities
            .Where(x => request.Path.StartsWithSegments(x.Route, out path))
            .FirstOrDefault();

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
            var expressionBuilder = new OtterApiExpressionBuilder(request.Query, apiEntity);

            var filterResult = expressionBuilder.BuildFilterResult();
            result.FilterExpression = filterResult.Filter;
            result.FilterValues = filterResult.Values;

            result.SortExpression = expressionBuilder.BuildSortResult();

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
        return new OtterApiRestController(dbContext, actionContext, objectModelValidator, registry.Options);
    }

    public IActionResultExecutor<ObjectResult> GetActionExecutor()
    {
        return serviceProvider.GetRequiredService<IActionResultExecutor<ObjectResult>>();
    }
}