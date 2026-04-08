using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Moq;
using OtterApi.Controllers;
using OtterApi.Models;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Controllers;

/// <summary>
/// Integration tests for per-entity query filters (.WithQueryFilter).
/// All tests use an in-memory database with TestItem entities.
/// </summary>
public class ControllerQueryFilterIntegrationTests : IDisposable
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private readonly TestDbContext _db;

    public ControllerQueryFilterIntegrationTests()
    {
        _db = DbContextFactory.CreateInMemory();
        _db.Items.AddRange(
            new TestItem { Id = 1, Name = "Alpha",   IsActive = true,  TenantId = 1 },
            new TestItem { Id = 2, Name = "Beta",     IsActive = true,  TenantId = 2 },
            new TestItem { Id = 3, Name = "Gamma",    IsActive = false, TenantId = 1 },
            new TestItem { Id = 4, Name = "Delta",    IsActive = false, TenantId = 2 },
            new TestItem { Id = 5, Name = "Epsilon",  IsActive = true,  TenantId = 1 });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose() => _db.Dispose();

    // ── Builder helpers ───────────────────────────────────────────────────────

    private OtterApiEntity BuildEntity()
    {
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        return options.Entity<TestItem>("/items").ExposePagedResult()
            .Build(typeof(TestDbContext), options);
    }

    private OtterApiEntity BuildEntityWithFilter(System.Linq.Expressions.Expression<Func<TestItem, bool>> predicate)
    {
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        return options.Entity<TestItem>("/items").ExposePagedResult()
            .WithQueryFilter(predicate)
            .Build(typeof(TestDbContext), options);
    }

    private OtterApiRestController BuildController(OtterApiEntity entity)
    {
        var httpCtx   = new DefaultHttpContext();
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var validator = new Mock<IObjectModelValidator>();
        validator.Setup(v => v.Validate(It.IsAny<ActionContext>(),
            It.IsAny<ValidationStateDictionary>(), It.IsAny<string>(), It.IsAny<object>()));
        return new OtterApiRestController(_db, actionCtx, validator.Object);
    }

    private static OtterApiRouteInfo CollectionRoute(OtterApiEntity entity) => new()
    {
        Entity = entity, IncludeExpression = []
    };

    private static OtterApiRouteInfo ByIdRoute(OtterApiEntity entity, string id) => new()
    {
        Entity = entity, Id = id, IncludeExpression = []
    };

    private static List<TestItem> Items(ObjectResult result) =>
        ((List<object>)result.Value!).Cast<TestItem>().ToList();

    // ══════════════════════════════════════════════════════════════════════════
    // GET collection
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_Collection_QueryFilter_HidesNonMatchingRecords()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        var result = await ctrl.GetAsync(CollectionRoute(entity));
        var items  = Items(result);

        // Only Alpha(1), Beta(2), Epsilon(5) are active — Gamma(3) and Delta(4) must be hidden
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.True(i.IsActive));
    }

    [Fact]
    public async Task GetAsync_Collection_WithoutFilter_ReturnsAllRecords()
    {
        var entity = BuildEntityWithFilter(_ => true);   // trivial filter — all pass
        var ctrl   = BuildController(entity);

        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(5, items.Count);
    }

    [Fact]
    public async Task GetAsync_Collection_NoFilter_ReturnsAllRecords_Regression()
    {
        // Entities without any WithQueryFilter must return all records unchanged
        var entity = BuildEntity();
        var ctrl   = BuildController(entity);

        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(5, items.Count);
    }

    [Fact]
    public async Task GetAsync_Collection_QueryFilter_CombinedWithUserFilter()
    {
        // Entity filter: IsActive. User filter: TenantId == 1
        // Expected: Alpha(1), Epsilon(5)  — active AND tenant 1
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        var route = CollectionRoute(entity);
        route.FilterExpression = "TenantId == @0";
        route.FilterValues     = [1];

        var items = Items(await ctrl.GetAsync(route));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => { Assert.True(i.IsActive); Assert.Equal(1, i.TenantId); });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET /count
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_Count_ReflectsQueryFilter()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        var route = CollectionRoute(entity);
        route.IsCount = true;

        var result = await ctrl.GetAsync(route);

        Assert.Equal(3, (int)result.Value!);  // Alpha, Beta, Epsilon
    }

    [Fact]
    public async Task GetAsync_Count_WithoutFilter_ReturnsTotal()
    {
        var entity = BuildEntity();
        var ctrl   = BuildController(entity);

        var route = CollectionRoute(entity);
        route.IsCount = true;

        var result = await ctrl.GetAsync(route);

        Assert.Equal(5, (int)result.Value!);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET /pagedresult
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_PagedResult_TotalReflectsQueryFilter()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        var route = CollectionRoute(entity);
        route.IsPageResult = true;
        route.Take         = 10;
        route.Page         = 1;

        var paged = (OtterApiPagedResult)(await ctrl.GetAsync(route)).Value!;

        Assert.Equal(3, paged.Total);
        Assert.Equal(3, paged.Items.Count);
        Assert.All(paged.Items.Cast<TestItem>(), i => Assert.True(i.IsActive));
    }

    [Fact]
    public async Task GetAsync_PagedResult_PageCountReflectsFilteredTotal()
    {
        // 3 active items, pageSize = 2 → 2 pages
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        var route = CollectionRoute(entity);
        route.IsPageResult = true;
        route.Take         = 2;
        route.Page         = 1;

        var paged = (OtterApiPagedResult)(await ctrl.GetAsync(route)).Value!;

        Assert.Equal(3, paged.Total);
        Assert.Equal(2, paged.PageCount);
        Assert.Equal(2, paged.Items.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET by Id
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_ById_ReturnsItem_WhenPassesFilter()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        var result = await ctrl.GetAsync(ByIdRoute(entity, "1"));  // Alpha — active

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, ((TestItem)result.Value!).Id);
    }

    [Fact]
    public async Task GetAsync_ById_Returns404_WhenFilteredOut()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        // Gamma (Id=3) exists in DB but IsActive=false — must be hidden
        var result = await ctrl.GetAsync(ByIdRoute(entity, "3"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAsync_ById_Returns404_ForNonExistentId_Regression()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        var result = await ctrl.GetAsync(ByIdRoute(entity, "999"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAsync_ById_NoFilter_ReturnsInactiveItem_Regression()
    {
        // Without a filter, inactive items must still be retrievable by Id
        var entity = BuildEntity();
        var ctrl   = BuildController(entity);

        var result = await ctrl.GetAsync(ByIdRoute(entity, "3"));  // Gamma — inactive

        Assert.IsType<OkObjectResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PUT — filtered-out record returns 404
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PutAsync_Returns404_WhenRecordFilteredOut()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        _db.ChangeTracker.Clear();
        var ctrl   = BuildController(entity);

        var route   = ByIdRoute(entity, "3");  // Gamma — inactive, filtered out
        var updated = new TestItem { Id = 3, Name = "GammaUpdated", IsActive = false, TenantId = 1 };

        var result = await ctrl.PutAsync(route, updated);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task PutAsync_Succeeds_WhenRecordPassesFilter()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        _db.ChangeTracker.Clear();
        var ctrl   = BuildController(entity);

        var route   = ByIdRoute(entity, "1");  // Alpha — active, passes filter
        var updated = new TestItem { Id = 1, Name = "AlphaUpdated", IsActive = true, TenantId = 1 };

        var result = await ctrl.PutAsync(route, updated);

        Assert.IsType<OkObjectResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PATCH — filtered-out record returns 404
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchAsync_Returns404_WhenRecordFilteredOut()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        _db.ChangeTracker.Clear();
        var ctrl   = BuildController(entity);

        var route = ByIdRoute(entity, "3");  // Gamma — inactive, filtered out
        var patch = JsonNode.Parse("{\"name\":\"GammaPatched\"}")!.AsObject();

        var result = await ctrl.PatchAsync(route, patch);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task PatchAsync_Succeeds_WhenRecordPassesFilter()
    {
        var entity = BuildEntityWithFilter(item => item.IsActive);
        _db.ChangeTracker.Clear();
        var ctrl   = BuildController(entity);

        var route = ByIdRoute(entity, "1");  // Alpha — active, passes filter
        var patch = JsonNode.Parse("{\"name\":\"AlphaPatched\"}")!.AsObject();

        var result = await ctrl.PatchAsync(route, patch);

        Assert.IsType<OkObjectResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Multiple filters chained — AND semantics
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_Collection_MultipleFilters_BothMustPass()
    {
        // Filter 1: IsActive. Filter 2: TenantId == 1
        // Expected: Alpha(1), Epsilon(5) — only they are active AND tenant 1
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive)
            .WithQueryFilter(i => i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        var ctrl  = BuildController(entity);
        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => { Assert.True(i.IsActive); Assert.Equal(1, i.TenantId); });
        Assert.Contains(items, i => i.Name == "Alpha");
        Assert.Contains(items, i => i.Name == "Epsilon");
    }

    [Fact]
    public async Task GetAsync_Collection_MultipleFilters_Count_BothMustPass()
    {
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive)
            .WithQueryFilter(i => i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        var ctrl  = BuildController(entity);
        var route = CollectionRoute(entity);
        route.IsCount = true;

        var result = await ctrl.GetAsync(route);

        Assert.Equal(2, (int)result.Value!);
    }

    [Fact]
    public async Task GetAsync_ById_MultipleFilters_Returns404_WhenAnyFails()
    {
        // Beta (Id=2): IsActive=true, TenantId=2 → passes filter1 but fails filter2 (TenantId==1)
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive)
            .WithQueryFilter(i => i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        var ctrl   = BuildController(entity);
        var result = await ctrl.GetAsync(ByIdRoute(entity, "2"));  // Beta

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAsync_ById_MultipleFilters_Returns200_WhenBothPass()
    {
        // Alpha (Id=1): IsActive=true, TenantId=1 → passes both filters
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive)
            .WithQueryFilter(i => i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        var ctrl   = BuildController(entity);
        var result = await ctrl.GetAsync(ByIdRoute(entity, "1"));  // Alpha

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, ((TestItem)result.Value!).Id);
    }

    [Fact]
    public async Task PutAsync_MultipleFilters_Returns404_WhenAnyFails()
    {
        // Beta (Id=2): IsActive=true but TenantId=2 — fails second filter
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive)
            .WithQueryFilter(i => i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        _db.ChangeTracker.Clear();
        var ctrl    = BuildController(entity);
        var updated = new TestItem { Id = 2, Name = "BetaUpdated", IsActive = true, TenantId = 2 };

        var result = await ctrl.PutAsync(ByIdRoute(entity, "2"), updated);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task PatchAsync_MultipleFilters_Returns404_WhenAnyFails()
    {
        // Beta (Id=2): IsActive=true but TenantId=2 — fails second filter
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive)
            .WithQueryFilter(i => i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        _db.ChangeTracker.Clear();
        var ctrl  = BuildController(entity);
        var patch = JsonNode.Parse("{\"name\":\"BetaPatched\"}")!.AsObject();

        var result = await ctrl.PatchAsync(ByIdRoute(entity, "2"), patch);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Single WithQueryFilter with compound condition (&&)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_Collection_SingleFilterWithCompoundCondition()
    {
        // One predicate, two conditions: IsActive && TenantId == 1
        // Expected same result as two chained filters: Alpha(1), Epsilon(5)
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive && i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        var ctrl  = BuildController(entity);
        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => { Assert.True(i.IsActive); Assert.Equal(1, i.TenantId); });
        Assert.Contains(items, i => i.Name == "Alpha");
        Assert.Contains(items, i => i.Name == "Epsilon");
    }

    [Fact]
    public async Task GetAsync_Count_SingleFilterWithCompoundCondition()
    {
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive && i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        var ctrl  = BuildController(entity);
        var route = CollectionRoute(entity);
        route.IsCount = true;

        var result = await ctrl.GetAsync(route);

        Assert.Equal(2, (int)result.Value!);
    }

    [Fact]
    public async Task GetAsync_ById_SingleFilterCompound_Returns404_WhenPartialMatch()
    {
        // Beta (Id=2): IsActive=true but TenantId=2 → partial match on && predicate → 404
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive && i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        var ctrl   = BuildController(entity);
        var result = await ctrl.GetAsync(ByIdRoute(entity, "2"));  // Beta — fails TenantId part

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAsync_ById_SingleFilterCompound_Returns200_WhenFullMatch()
    {
        // Alpha (Id=1): IsActive=true, TenantId=1 → passes compound predicate
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive && i.TenantId == 1)
            .Build(typeof(TestDbContext), options);

        var ctrl   = BuildController(entity);
        var result = await ctrl.GetAsync(ByIdRoute(entity, "1"));  // Alpha

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, ((TestItem)result.Value!).Id);
    }

    [Fact]
    public async Task GetAsync_Collection_SingleFilterWithOrCondition()
    {
        // One predicate with OR: IsActive || TenantId == 2
        // Alpha(active,t1), Beta(active,t2), Gamma(inactive,t1) excluded, Delta(inactive,t2), Epsilon(active,t1)
        // Passes: Alpha, Beta, Delta, Epsilon — 4 items
        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive || i.TenantId == 2)
            .Build(typeof(TestDbContext), options);

        var ctrl  = BuildController(entity);
        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(4, items.Count);
        Assert.DoesNotContain(items, i => i.Name == "Gamma");  // inactive AND TenantId==1 — only excluded item
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Filter + sort + pagination — filters apply first, then the rest
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_FilterAndSort_SortAppliedOnFilteredSubset()
    {
        // Active items: Alpha(1), Beta(2), Epsilon(5) — sort by Name asc → Alpha, Beta, Epsilon
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        var route = CollectionRoute(entity);
        route.SortExpression = "Name asc";

        var names = Items(await ctrl.GetAsync(route)).Select(i => i.Name).ToList();

        Assert.Equal(new[] { "Alpha", "Beta", "Epsilon" }, names);
    }

    [Fact]
    public async Task GetAsync_FilterAndSkipTake_PaginationAppliedOnFilteredSubset()
    {
        // Active items sorted by Id: Alpha(1), Beta(2), Epsilon(5). Skip 1, take 2 → Beta, Epsilon
        var entity = BuildEntityWithFilter(item => item.IsActive);
        var ctrl   = BuildController(entity);

        var route = CollectionRoute(entity);
        route.SortExpression = "Id asc";
        route.Skip           = 1;
        route.Take           = 2;

        var ids = Items(await ctrl.GetAsync(route)).Select(i => i.Id).ToList();

        Assert.Equal(new[] { 2, 5 }, ids);
    }
}

