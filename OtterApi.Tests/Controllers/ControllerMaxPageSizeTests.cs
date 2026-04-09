using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Moq;
using OtterApi.Configs;
using OtterApi.Controllers;
using OtterApi.Models;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Controllers;

/// <summary>
/// Verifies that MaxPageSize in OtterApiOptions clamps client-supplied page sizes
/// for regular list requests, pagedresult, and custom routes.
/// </summary>
public class ControllerMaxPageSizeTests : IDisposable
{
    // Dataset: 5 products (Ids 1-5)
    private readonly TestDbContext _db;

    public ControllerMaxPageSizeTests()
    {
        _db = DbContextFactory.CreateInMemory();
        _db.Products.AddRange(
            new TestProduct { Id = 1, Name = "P1", Price = 1m, CategoryId = 1 },
            new TestProduct { Id = 2, Name = "P2", Price = 2m, CategoryId = 1 },
            new TestProduct { Id = 3, Name = "P3", Price = 3m, CategoryId = 1 },
            new TestProduct { Id = 4, Name = "P4", Price = 4m, CategoryId = 1 },
            new TestProduct { Id = 5, Name = "P5", Price = 5m, CategoryId = 1 });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (OtterApiRestController ctrl, OtterApiEntity entity) Build(int maxPageSize)
    {
        var options = new OtterApiOptions { Path = "/api", MaxPageSize = maxPageSize };
        var entity  = options.Entity<TestProduct>("/products").ExposePagedResult()
                             .Build(typeof(TestDbContext), options);
        var registry = new OtterApiRegistry([entity], options);

        var httpCtx   = new DefaultHttpContext();
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var validator = new Mock<IObjectModelValidator>();
        validator.Setup(v => v.Validate(
            It.IsAny<ActionContext>(), It.IsAny<ValidationStateDictionary>(),
            It.IsAny<string>(), It.IsAny<object>()));

        var ctrl = new OtterApiRestController(
            dbContext:            _db,
            actionContext:        actionCtx,
            objectModelValidator: validator.Object,
            serviceProvider:      null,
            registry:             registry);

        return (ctrl, entity);
    }

    private static List<TestProduct> Items(ObjectResult r) =>
        ((List<object>)r.Value!).Cast<TestProduct>().ToList();

    // ── Regular list (take) ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_Take_ClampsToMaxPageSize_WhenExceedsLimit()
    {
        var (ctrl, entity) = Build(maxPageSize: 3);

        // client requests take=100, but MaxPageSize=3
        var route = new OtterApiRouteInfo { Entity = entity, Take = 100 };
        var result = await ctrl.GetAsync(route);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(3, Items(result).Count);
    }

    [Fact]
    public async Task GetAsync_Take_NotClamped_WhenBelowLimit()
    {
        var (ctrl, entity) = Build(maxPageSize: 3);

        // client requests take=2, which is under MaxPageSize=3 → no clamp
        var route = new OtterApiRouteInfo { Entity = entity, Take = 2 };
        var result = await ctrl.GetAsync(route);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, Items(result).Count);
    }

    [Fact]
    public async Task GetAsync_Take_NoLimit_WhenMaxPageSizeIsZero()
    {
        var (ctrl, entity) = Build(maxPageSize: 0);   // 0 = no limit

        // client requests take=100 — all 5 products should come back
        var route = new OtterApiRouteInfo { Entity = entity, Take = 100 };
        var result = await ctrl.GetAsync(route);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(5, Items(result).Count);
    }

    // ── PagedResult ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_PagedResult_ClampsPageSizeToMax_WhenExceedsLimit()
    {
        var (ctrl, entity) = Build(maxPageSize: 3);

        // take=100 in routeInfo → pageSize would be 100, clamped to 3
        var route = new OtterApiRouteInfo { Entity = entity, Take = 100, IsPageResult = true, Page = 1 };
        var result = await ctrl.GetAsync(route);

        Assert.IsType<OkObjectResult>(result);
        var paged = (OtterApiPagedResult)result.Value!;
        Assert.Equal(3, paged.Items.Count);
        Assert.Equal(3, paged.PageSize);
    }

    [Fact]
    public async Task GetAsync_PagedResult_DefaultPageSize_ClampsToMax()
    {
        var (ctrl, entity) = Build(maxPageSize: 3);

        // take=0 → default pageSize=10, clamped to MaxPageSize=3
        var route = new OtterApiRouteInfo { Entity = entity, Take = 0, IsPageResult = true, Page = 1 };
        var result = await ctrl.GetAsync(route);

        Assert.IsType<OkObjectResult>(result);
        var paged = (OtterApiPagedResult)result.Value!;
        Assert.Equal(3, paged.Items.Count);
        Assert.Equal(3, paged.PageSize);
    }

    [Fact]
    public async Task GetAsync_PagedResult_NotClamped_WhenBelowLimit()
    {
        var (ctrl, entity) = Build(maxPageSize: 10);

        // take=2, MaxPageSize=10 → no clamp
        var route = new OtterApiRouteInfo { Entity = entity, Take = 2, IsPageResult = true, Page = 1 };
        var result = await ctrl.GetAsync(route);

        Assert.IsType<OkObjectResult>(result);
        var paged = (OtterApiPagedResult)result.Value!;
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal(2, paged.PageSize);
    }
}


