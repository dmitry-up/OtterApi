using System.Linq.Expressions;
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
/// Integration tests for named custom GET routes registered via .WithCustomRoute(...).
/// Dataset (5 TestItems):
///   Id=1  Alpha    IsActive=true   TenantId=1
///   Id=2  Beta     IsActive=true   TenantId=2
///   Id=3  Gamma    IsActive=false  TenantId=1
///   Id=4  Delta    IsActive=false  TenantId=2
///   Id=5  Epsilon  IsActive=true   TenantId=1
/// </summary>
public class ControllerCustomRouteIntegrationTests : IDisposable
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private readonly TestDbContext _db;

    public ControllerCustomRouteIntegrationTests()
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

    private OtterApi.Configs.OtterApiOptions Options() =>
        new() { Path = "/api" };

    /// <summary>Entity with a single custom route and no entity-level QueryFilter.</summary>
    private OtterApiEntity BuildEntityWithCustomRoute(
        string slug,
        Expression<Func<TestItem, bool>>? filter = null,
        string? sort = null,
        int take = 0,
        bool single = false)
    {
        var opts = Options();
        return opts.Entity<TestItem>("/items")
            .WithCustomRoute(slug, filter: filter, sort: sort, take: take, single: single)
            .Build(typeof(TestDbContext), opts);
    }

    /// <summary>Entity with a global QueryFilter AND a custom route.</summary>
    private OtterApiEntity BuildEntityWithQueryFilterAndCustomRoute(
        Expression<Func<TestItem, bool>> queryFilter,
        string slug,
        Expression<Func<TestItem, bool>>? routeFilter = null,
        string? sort = null)
    {
        var opts = Options();
        return opts.Entity<TestItem>("/items")
            .WithQueryFilter(queryFilter)
            .WithCustomRoute(slug, filter: routeFilter, sort: sort)
            .Build(typeof(TestDbContext), opts);
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

    private static OtterApiRouteInfo RouteFor(OtterApiEntity entity, string slug) => new()
    {
        Entity          = entity,
        IncludeExpression = [],
        CustomRoute     = entity.CustomRoutes.First(r => r.Slug == slug)
    };

    private static List<TestItem> Items(ObjectResult result) =>
        ((List<object>)result.Value!).Cast<TestItem>().ToList();

    // ══════════════════════════════════════════════════════════════════════════
    // Single = true
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_CustomRoute_Single_ReturnsFirstMatch()
    {
        // "last-active": active items sorted by Name asc → Alpha is first
        var entity = BuildEntityWithCustomRoute("last-active",
            filter: i => i.IsActive,
            sort:   "Name asc",
            single: true);

        var result = await BuildController(entity).GetAsync(RouteFor(entity, "last-active"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Alpha", ((TestItem)result.Value!).Name);
    }

    [Fact]
    public async Task GetAsync_CustomRoute_Single_Returns404_WhenNoMatch()
    {
        // filter matches nothing
        var entity = BuildEntityWithCustomRoute("none",
            filter: i => i.TenantId == 999,
            single: true);

        var result = await BuildController(entity).GetAsync(RouteFor(entity, "none"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAsync_CustomRoute_Single_SortDeterminesWhichItemIsReturned()
    {
        // Active items sorted by Name desc → Epsilon is first
        var entity = BuildEntityWithCustomRoute("latest",
            filter: i => i.IsActive,
            sort:   "Name desc",
            single: true);

        var result = await BuildController(entity).GetAsync(RouteFor(entity, "latest"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Epsilon", ((TestItem)result.Value!).Name);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Single = false (list)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_CustomRoute_List_ReturnsFilteredSubset()
    {
        // active items only
        var entity = BuildEntityWithCustomRoute("active", filter: i => i.IsActive);
        var items  = Items(await BuildController(entity).GetAsync(RouteFor(entity, "active")));

        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.True(i.IsActive));
    }

    [Fact]
    public async Task GetAsync_CustomRoute_List_NoFilter_ReturnsAll()
    {
        // no filter — all 5 items
        var entity = BuildEntityWithCustomRoute("all");
        var items  = Items(await BuildController(entity).GetAsync(RouteFor(entity, "all")));

        Assert.Equal(5, items.Count);
    }

    [Fact]
    public async Task GetAsync_CustomRoute_List_EmptyResult_ReturnsEmptyArray()
    {
        var entity = BuildEntityWithCustomRoute("empty", filter: i => i.TenantId == 999);
        var items  = Items(await BuildController(entity).GetAsync(RouteFor(entity, "empty")));

        Assert.Empty(items);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Take
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_CustomRoute_Take_LimitsResultCount()
    {
        // 3 active, take=2
        var entity = BuildEntityWithCustomRoute("top2",
            filter: i => i.IsActive,
            sort:   "Name asc",
            take:   2);

        var items = Items(await BuildController(entity).GetAsync(RouteFor(entity, "top2")));

        Assert.Equal(2, items.Count);
        Assert.Equal("Alpha", items[0].Name);
        Assert.Equal("Beta",  items[1].Name);
    }

    [Fact]
    public async Task GetAsync_CustomRoute_Take1_ReturnsSingleItemAsArray()
    {
        // take=1, single=false → still returns array with one element
        var entity = BuildEntityWithCustomRoute("first",
            filter: i => i.IsActive,
            sort:   "Name asc",
            take:   1,
            single: false);

        var items = Items(await BuildController(entity).GetAsync(RouteFor(entity, "first")));

        Assert.Single(items);
        Assert.Equal("Alpha", items[0].Name);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sort
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_CustomRoute_Sort_AscendingNameOrder()
    {
        var entity = BuildEntityWithCustomRoute("active-asc",
            filter: i => i.IsActive,
            sort:   "Name asc");

        var names = Items(await BuildController(entity).GetAsync(RouteFor(entity, "active-asc")))
            .Select(i => i.Name).ToList();

        Assert.Equal(new[] { "Alpha", "Beta", "Epsilon" }, names);
    }

    [Fact]
    public async Task GetAsync_CustomRoute_Sort_DescendingNameOrder()
    {
        var entity = BuildEntityWithCustomRoute("active-desc",
            filter: i => i.IsActive,
            sort:   "Name desc");

        var names = Items(await BuildController(entity).GetAsync(RouteFor(entity, "active-desc")))
            .Select(i => i.Name).ToList();

        Assert.Equal(new[] { "Epsilon", "Beta", "Alpha" }, names);
    }

    [Fact]
    public async Task GetAsync_CustomRoute_NoSort_DefaultsToIdDesc()
    {
        // no sort specified → default Id desc
        var entity = BuildEntityWithCustomRoute("all");
        var ids    = Items(await BuildController(entity).GetAsync(RouteFor(entity, "all")))
            .Select(i => i.Id).ToList();

        Assert.Equal(ids.OrderByDescending(x => x).ToList(), ids);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Entity-level QueryFilter + custom route filter — stacking
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_CustomRoute_EntityQueryFilter_AppliedFirst()
    {
        // Entity filter: IsActive (hides Gamma, Delta)
        // Custom route filter: TenantId==1
        // Result: Alpha(1,t1), Epsilon(5,t1) — active AND tenant 1
        var entity = BuildEntityWithQueryFilterAndCustomRoute(
            queryFilter: i => i.IsActive,
            slug:        "tenant1",
            routeFilter: i => i.TenantId == 1);

        var items = Items(await BuildController(entity).GetAsync(RouteFor(entity, "tenant1")));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => { Assert.True(i.IsActive); Assert.Equal(1, i.TenantId); });
    }

    [Fact]
    public async Task GetAsync_CustomRoute_EntityQueryFilter_HidesRecords_EvenWithoutRouteFilter()
    {
        // Entity filter: IsActive. No custom route filter.
        // Custom route "all" should still only return active items.
        var entity = BuildEntityWithQueryFilterAndCustomRoute(
            queryFilter: i => i.IsActive,
            slug:        "all");

        var items = Items(await BuildController(entity).GetAsync(RouteFor(entity, "all")));

        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.True(i.IsActive));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Client query parameters work on custom routes
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_CustomRoute_ClientFilter_CombinesWithRouteFilter()
    {
        // Route filter: IsActive. Client adds TenantId==1.
        // Expected: Alpha(1), Epsilon(5)
        var entity = BuildEntityWithCustomRoute("active", filter: i => i.IsActive);
        var ctrl   = BuildController(entity);

        var route = RouteFor(entity, "active");
        route.FilterApply = q => ((IQueryable<TestItem>)q).Where(i => i.TenantId == 1);

        var items = Items(await ctrl.GetAsync(route));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => { Assert.True(i.IsActive); Assert.Equal(1, i.TenantId); });
    }

    [Fact]
    public async Task GetAsync_CustomRoute_ClientSort_OverridesRouteSort()
    {
        // Route sort: Name desc. Client requests Name asc.
        // Expected order: Alpha, Beta, Epsilon (client sort wins)
        var entity = BuildEntityWithCustomRoute("active",
            filter: i => i.IsActive,
            sort:   "Name desc");

        var route = RouteFor(entity, "active");
        route.SortApply = q => ((IQueryable<TestItem>)q).OrderBy(i => i.Name);   // overrides route sort

        var names = Items(await BuildController(entity).GetAsync(route))
            .Select(i => i.Name).ToList();

        Assert.Equal(new[] { "Alpha", "Beta", "Epsilon" }, names);
    }

    [Fact]
    public async Task GetAsync_CustomRoute_ClientTake_OverridesRouteTake()
    {
        // Route take=2. Client requests take=1.
        var entity = BuildEntityWithCustomRoute("active",
            filter: i => i.IsActive,
            sort:   "Name asc",
            take:   2);

        var route = RouteFor(entity, "active");
        route.Take = 1;   // client take overrides

        var items = Items(await BuildController(entity).GetAsync(route));

        Assert.Single(items);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Multiple custom routes on the same entity
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_MultipleCustomRoutes_EachWorksIndependently()
    {
        var opts   = Options();
        var entity = opts.Entity<TestItem>("/items")
            .WithCustomRoute("active",   filter: i => i.IsActive)
            .WithCustomRoute("inactive", filter: i => !i.IsActive)
            .Build(typeof(TestDbContext), opts);

        var ctrl = BuildController(entity);

        var active   = Items(await ctrl.GetAsync(RouteFor(entity, "active")));
        var inactive = Items(await ctrl.GetAsync(RouteFor(entity, "inactive")));

        Assert.Equal(3, active.Count);
        Assert.Equal(2, inactive.Count);
        Assert.All(active,   i => Assert.True(i.IsActive));
        Assert.All(inactive, i => Assert.False(i.IsActive));
    }

    [Fact]
    public async Task GetAsync_MultipleCustomRoutes_DifferentSortAndTake()
    {
        var opts   = Options();
        var entity = opts.Entity<TestItem>("/items")
            .WithCustomRoute("top1-by-name", filter: i => i.IsActive, sort: "Name asc",  take: 1, single: true)
            .WithCustomRoute("top1-by-id",   filter: i => i.IsActive, sort: "Id desc",   take: 1, single: true)
            .Build(typeof(TestDbContext), opts);

        var ctrl = BuildController(entity);

        var byName = (TestItem)(await ctrl.GetAsync(RouteFor(entity, "top1-by-name"))).Value!;
        var byId   = (TestItem)(await ctrl.GetAsync(RouteFor(entity, "top1-by-id"))).Value!;

        Assert.Equal("Alpha",   byName.Name);   // Name asc → Alpha
        Assert.Equal("Epsilon", byId.Name);     // Id desc → Id=5 = Epsilon
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Slug validation at build time
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("count")]
    [InlineData("COUNT")]
    [InlineData("pagedresult")]
    [InlineData("PagedResult")]
    public void Build_ThrowsInvalidOperation_WhenSlugConflictsWithReservedPath(string reservedSlug)
    {
        var opts = Options();
        var ex   = Assert.Throws<InvalidOperationException>(() =>
            opts.Entity<TestItem>("/items")
                .WithCustomRoute(reservedSlug)
                .Build(typeof(TestDbContext), opts));

        Assert.Contains(reservedSlug.ToLowerInvariant(), ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Build_ThrowsInvalidOperation_WhenDuplicateSlugsRegistered()
    {
        var opts = Options();
        var ex   = Assert.Throws<InvalidOperationException>(() =>
            opts.Entity<TestItem>("/items")
                .WithCustomRoute("active", filter: i => i.IsActive)
                .WithCustomRoute("active", filter: i => i.TenantId == 1)
                .Build(typeof(TestDbContext), opts));

        Assert.Contains("active", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Build_SlugIsNormalized_ToLowerWithoutSlashes()
    {
        var opts   = Options();
        var entity = opts.Entity<TestItem>("/items")
            .WithCustomRoute("/Featured/", filter: i => i.IsActive)
            .Build(typeof(TestDbContext), opts);

        // Slug stored as "featured" regardless of original casing / slashes
        Assert.Single(entity.CustomRoutes);
        Assert.Equal("featured", entity.CustomRoutes[0].Slug);
    }
}

