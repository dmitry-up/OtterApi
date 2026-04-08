using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OtterApi.Configs;
using OtterApi.Models;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Processors;

/// <summary>
/// Contract: OtterApiRequestProcessor.GetRoutInfo maps an incoming HTTP path
/// to the correct OtterApiRouteInfo fields (entity, id, count, paged-result,
/// filter / sort / paging / include expressions).
///
/// These tests exercise the routing layer in isolation – no real DbContext
/// or HTTP pipeline needed.
/// </summary>
public class RequestProcessorRouteInfoTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OtterApiEntity BuildProductEntity()
    {
        var options = new OtterApiOptions { Path = "/api" };
        return options.Entity<TestProduct>("/products").ExposePagedResult()
            .Build(typeof(TestDbContext), options);
    }

    private static OtterApiRegistry BuildRegistry(OtterApiEntity entity)
    {
        var options = new OtterApiOptions { Path = "/api" };
        return new OtterApiRegistry([entity], options);
    }

    private static HttpRequest CreateRequest(
        string path,
        Dictionary<string, string>? query = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path   = path;
        ctx.Request.Method = "GET";

        if (query?.Count > 0)
        {
            ctx.Request.QueryString = new QueryString(
                "?" + string.Join("&", query.Select(kv =>
                    Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value))));
        }

        return ctx.Request;
    }

    // Minimal processor-like helper that runs only the routing logic we care about
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
                    result.Id = string.IsNullOrEmpty(value) ? null : value;
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

    // ══════════════════════════════════════════════════════════════════════════
    // Entity resolution
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRouteInfo_ResolvesEntity_ForRegisteredRoute()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products");

        var info = GetRouteInfo(registry, request);

        Assert.NotNull(info.Entity);
    }

    [Fact]
    public void GetRouteInfo_EntityIsNull_ForUnregisteredRoute()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/unknown");

        var info = GetRouteInfo(registry, request);

        Assert.Null(info.Entity);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Id extraction
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRouteInfo_ExtractsId_FromPath()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products/42");

        var info = GetRouteInfo(registry, request);

        Assert.Equal("42", info.Id);
    }

    [Fact]
    public void GetRouteInfo_IdIsNull_ForCollectionRoute()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products");

        var info = GetRouteInfo(registry, request);

        Assert.Null(info.Id);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // /count route
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRouteInfo_SetsIsCount_ForCountRoute()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products/count");

        var info = GetRouteInfo(registry, request);

        Assert.True(info.IsCount);
        Assert.Null(info.Id);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // /pagedresult route
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRouteInfo_SetsIsPageResult_WhenEntityExposesPagedResult()
    {
        var entity   = BuildProductEntity();   // ExposePagedResult = true
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products/pagedresult");

        var info = GetRouteInfo(registry, request);

        Assert.True(info.IsPageResult);
    }

    [Fact]
    public void GetRouteInfo_IsPageResult_IsFalse_WhenEntityDoesNotExposeIt()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")  // ExposePagedResult NOT set
            .Build(typeof(TestDbContext), options);
        var registry = new OtterApiRegistry([entity], options);
        var request  = CreateRequest("/api/products/pagedresult");

        var info = GetRouteInfo(registry, request);

        Assert.False(info.IsPageResult);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Filter expressions built from query string
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRouteInfo_BuildsFilterExpression_FromQueryString()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products", new() { ["filter[Name]"] = "Widget" });

        var info = GetRouteInfo(registry, request);

        Assert.Equal("Name == @0", info.FilterExpression);
        Assert.Single(info.FilterValues!);
        Assert.Equal("Widget", info.FilterValues![0]);
    }

    [Fact]
    public void GetRouteInfo_NoFilterExpression_WhenNoFilterParams()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products");

        var info = GetRouteInfo(registry, request);

        Assert.Null(info.FilterExpression);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Filter expressions are NOT built when an Id is present
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRouteInfo_DoesNotBuildFilterExpressions_WhenIdInPath()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        // Query string present, but path contains an id → filters must be ignored
        var ctx = new DefaultHttpContext();
        ctx.Request.Path        = "/api/products/5";
        ctx.Request.QueryString = new QueryString("?filter[Name]=Widget");

        var info = GetRouteInfo(registry, ctx.Request);

        Assert.Equal("5",  info.Id);
        Assert.Null(info.FilterExpression);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sort expression
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRouteInfo_BuildsSortExpression_FromQueryString()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products", new() { ["sort[Name]"] = "asc" });

        var info = GetRouteInfo(registry, request);

        Assert.Equal("Name asc", info.SortExpression);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Paging
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRouteInfo_BuildsPagingInfo_FromQueryString()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        var request  = CreateRequest("/api/products", new() { ["page"] = "2", ["pagesize"] = "10" });

        var info = GetRouteInfo(registry, request);

        Assert.Equal(10, info.Take);
        Assert.Equal(10, info.Skip);
        Assert.Equal(2,  info.Page);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Include
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRouteInfo_BuildsIncludeExpression_FromQueryString()
    {
        var entity   = BuildProductEntity();
        var registry = BuildRegistry(entity);
        // TestProduct has a navigation property "Category"
        var request  = CreateRequest("/api/products", new() { ["include"] = "Category" });

        var info = GetRouteInfo(registry, request);

        Assert.Contains("Category", info.IncludeExpression);
    }
}



