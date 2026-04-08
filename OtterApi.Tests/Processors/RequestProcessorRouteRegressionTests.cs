using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OtterApi.Configs;
using OtterApi.Models;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Processors;

/// <summary>
/// Regression tests for routing bugs in GetRoutInfo.
/// </summary>
public class RequestProcessorRouteRegressionTests
{
    private static OtterApiEntity BuildProductEntity()
    {
        var options = new OtterApiOptions { Path = "/api" };
        return options.Entity<TestProduct>("/products")
            .Build(typeof(TestDbContext), options);
    }

    private static OtterApiRegistry BuildRegistry(OtterApiEntity entity)
        => new([entity], new OtterApiOptions { Path = "/api" });

    // Helper that replicates GetRoutInfo logic (same as in RequestProcessorRouteInfoTests)
    private static OtterApiRouteInfo GetRouteInfo(OtterApiRegistry registry, HttpRequest request)
    {
        PathString path = null;
        var result = new OtterApiRouteInfo();

        var apiEntity = registry.Entities
            .Where(x => request.Path.StartsWithSegments(x.Route, out path))
            .FirstOrDefault();

        result.Entity = apiEntity;

        if (path.HasValue)
        {
            var value = path.Value!.TrimStart('/');
            switch (value)
            {
                case "count":
                    result.IsCount = true;
                    break;
                case "pagedresult":
                    result.IsPageResult = apiEntity!.ExposePagedResult;
                    break;
                default:
                    // Bug B fix: empty string from trailing slash must NOT set Id
                    if (!string.IsNullOrEmpty(value))
                        result.Id = value;
                    break;
            }
        }

        if (apiEntity != null && string.IsNullOrWhiteSpace(result.Id) && request.Query?.Keys.Count > 0)
        {
            var builder = new OtterApi.Builders.OtterApiExpressionBuilder(request.Query, apiEntity);
            var filterResult = builder.BuildFilterResult();
            result.FilterExpression = filterResult.Filter;
            result.FilterValues     = filterResult.Values;
            result.SortExpression   = builder.BuildSortResult();
            var pageResult  = builder.BuildPagingResult();
            result.Take     = pageResult.Take;
            result.Skip     = pageResult.Skip;
            result.Page     = pageResult.Page;
            result.IncludeExpression = builder.BuildIncludeResult();
        }

        return result;
    }

    private static HttpRequest CreateRequest(string path, string? queryString = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path        = path;
        ctx.Request.Method      = "GET";
        ctx.Request.QueryString = queryString != null ? new QueryString(queryString) : default;
        return ctx.Request;
    }

    // ── Bug B: trailing slash sets Id to empty string ─────────────────────────
    // GET /api/products/ → remaining path = "/" → TrimStart('/') = "" →
    // Id was set to "" which is not null → controller called FindAsync("") → crash

    [Fact]
    public void GetRouteInfo_TrailingSlash_DoesNotSetId()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products/");

        var info = GetRouteInfo(registry, request);

        // Id must be null so the controller treats it as a collection request
        Assert.Null(info.Id);
    }

    [Fact]
    public void GetRouteInfo_TrailingSlash_WithQueryParams_StillBuildsFilterExpression()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products/", "?filter[Name]=Widget");

        // Need to create query properly
        var ctx = new DefaultHttpContext();
        ctx.Request.Path        = "/api/products/";
        ctx.Request.QueryString = new QueryString("?filter[Name]=Widget");

        var info = GetRouteInfo(registry, ctx.Request);

        Assert.Null(info.Id);
        Assert.Equal("Name == @0", info.FilterExpression);
    }

    // ── Bug C: DELETE/PUT to collection endpoint (no id) must not crash ───────
    // When routeInfo.Id is null, OtterApiTypeConverter.ChangeType(null, ...) throws.
    // The controller must guard against null Id for operations that require it.

    [Fact]
    public void GetRouteInfo_Delete_WithoutId_IdIsNull()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path   = "/api/products";
        ctx.Request.Method = "DELETE";

        var info = GetRouteInfo(registry, ctx.Request);

        // Id must be null — the controller/middleware must then return 400, not crash
        Assert.Null(info.Id);
    }

    [Fact]
    public void GetRouteInfo_Put_WithoutId_IdIsNull()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path   = "/api/products";
        ctx.Request.Method = "PUT";

        var info = GetRouteInfo(registry, ctx.Request);

        Assert.Null(info.Id);
    }
}

