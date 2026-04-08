using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OtterApi.Controllers;
using OtterApi.Enums;
using OtterApi.Models;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Controllers;

/// <summary>
/// Tests for:
///   1. Chained BeforeSave / AfterSave handlers — multiple .BeforeSave() calls all run in order.
///   2. WithScopedQueryFilter — predicate resolved at runtime via IServiceProvider.
/// </summary>
public class ControllerHandlerChainAndScopedFilterTests : IDisposable
{
    private readonly TestDbContext _db;

    public ControllerHandlerChainAndScopedFilterTests()
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private OtterApiRestController BuildController(OtterApi.Models.OtterApiEntity entity,
        IServiceProvider? sp = null)
    {
        var httpCtx   = new DefaultHttpContext();
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var validator = new Mock<IObjectModelValidator>();
        validator.Setup(v => v.Validate(It.IsAny<ActionContext>(),
            It.IsAny<ValidationStateDictionary>(), It.IsAny<string>(), It.IsAny<object>()));
        return new OtterApiRestController(_db, actionCtx, validator.Object, sp);
    }

    private static OtterApiRouteInfo CollectionRoute(OtterApi.Models.OtterApiEntity entity) => new()
    {
        Entity = entity, IncludeExpression = []
    };

    private static OtterApiRouteInfo PostRoute(OtterApi.Models.OtterApiEntity entity) => new()
    {
        Entity = entity, IncludeExpression = []
    };

    private static List<TestItem> Items(ObjectResult result) =>
        ((List<object>)result.Value!).Cast<TestItem>().ToList();

    // ══════════════════════════════════════════════════════════════════════════
    // Chained BeforeSave handlers
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostAsync_MultipleBeforeSave_AllRunInOrder()
    {
        var callOrder = new List<int>();

        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .BeforeSave((_, _, _, _) => { callOrder.Add(1); })
            .BeforeSave((_, _, _, _) => { callOrder.Add(2); })
            .BeforeSave((_, _, _, _) => { callOrder.Add(3); })
            .Build(typeof(TestDbContext), opts);

        var ctrl = BuildController(entity);
        var item = new TestItem { Id = 10, Name = "New", IsActive = true, TenantId = 1 };

        await ctrl.PostAsync(PostRoute(entity), item);

        Assert.Equal([1, 2, 3], callOrder);
    }

    [Fact]
    public async Task PostAsync_MultipleBeforeSave_EachReceivesCorrectEntity()
    {
        string? capturedName = null;
        int?    capturedId   = null;

        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .BeforeSave((_, item, _, _) => { item.Name = "Modified"; })
            .BeforeSave((_, item, _, _) => { capturedName = item.Name; capturedId = item.Id; })
            .Build(typeof(TestDbContext), opts);

        var ctrl = BuildController(entity);
        var newItem = new TestItem { Id = 11, Name = "Original", IsActive = true, TenantId = 1 };

        await ctrl.PostAsync(PostRoute(entity), newItem);

        // Second handler sees the state left by the first handler
        Assert.Equal("Modified", capturedName);
        Assert.Equal(11, capturedId);
    }

    [Fact]
    public async Task PostAsync_MultipleAfterSave_AllRunInOrder()
    {
        var callOrder = new List<string>();

        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .AfterSave((_, _, _, _) => { callOrder.Add("first"); })
            .AfterSave((_, _, _, _) => { callOrder.Add("second"); })
            .Build(typeof(TestDbContext), opts);

        var ctrl = BuildController(entity);
        var item = new TestItem { Id = 12, Name = "X", IsActive = true, TenantId = 1 };

        await ctrl.PostAsync(PostRoute(entity), item);

        Assert.Equal(["first", "second"], callOrder);
    }

    [Fact]
    public async Task PostAsync_BeforeSaveAndAfterSave_BothChainedIndependently()
    {
        var log = new List<string>();

        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .BeforeSave((_, _, _, _) => { log.Add("before-1"); })
            .BeforeSave((_, _, _, _) => { log.Add("before-2"); })
            .AfterSave((_, _, _, _)  => { log.Add("after-1"); })
            .AfterSave((_, _, _, _)  => { log.Add("after-2"); })
            .Build(typeof(TestDbContext), opts);

        var ctrl = BuildController(entity);
        var item = new TestItem { Id = 13, Name = "Y", IsActive = true, TenantId = 1 };

        await ctrl.PostAsync(PostRoute(entity), item);

        Assert.Equal(["before-1", "before-2", "after-1", "after-2"], log);
    }

    [Fact]
    public async Task PostAsync_SingleBeforeSave_StillWorksAfterRefactor()
    {
        // Regression: single handler registration must still work as before
        var called = false;

        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .BeforeSave((_, _, _, _) => { called = true; })
            .Build(typeof(TestDbContext), opts);

        var ctrl = BuildController(entity);
        var item = new TestItem { Id = 14, Name = "Z", IsActive = true, TenantId = 1 };

        await ctrl.PostAsync(PostRoute(entity), item);

        Assert.True(called);
    }

    [Fact]
    public async Task PostAsync_NoHandlers_WorksWithoutError()
    {
        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .Build(typeof(TestDbContext), opts);

        var ctrl = BuildController(entity);
        var item = new TestItem { Id = 15, Name = "W", IsActive = true, TenantId = 1 };

        var result = await ctrl.PostAsync(PostRoute(entity), item);

        Assert.IsType<CreatedResult>(result);
    }

    [Fact]
    public async Task PutAsync_MultipleBeforeSave_AllRun()
    {
        var callCount = 0;

        _db.ChangeTracker.Clear();

        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .BeforeSave((_, _, _, _) => { callCount++; })
            .BeforeSave((_, _, _, _) => { callCount++; })
            .Build(typeof(TestDbContext), opts);

        var ctrl    = BuildController(entity);
        var updated = new TestItem { Id = 1, Name = "AlphaUpdated", IsActive = true, TenantId = 1 };
        var route   = new OtterApiRouteInfo { Entity = entity, Id = "1", IncludeExpression = [] };

        await ctrl.PutAsync(route, updated);

        Assert.Equal(2, callCount);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WithScopedQueryFilter
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Simple fake service used in scoped filter tests.</summary>
    private interface ITenantProvider { int TenantId { get; } }
    private sealed class FakeTenantProvider(int tenantId) : ITenantProvider
    {
        public int TenantId { get; } = tenantId;
    }

    private IServiceProvider BuildSp(int tenantId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantProvider>(new FakeTenantProvider(tenantId));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetAsync_ScopedFilter_FiltersBasedOnRuntimeService()
    {
        // TenantId=1 → should see Alpha(1), Gamma(3), Epsilon(5)
        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .WithScopedQueryFilter(sp =>
            {
                var tenant = sp.GetRequiredService<ITenantProvider>();
                return item => item.TenantId == tenant.TenantId;
            })
            .Build(typeof(TestDbContext), opts);

        var sp   = BuildSp(tenantId: 1);
        var ctrl = BuildController(entity, sp);

        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal(1, i.TenantId));
    }

    [Fact]
    public async Task GetAsync_ScopedFilter_DifferentTenantIdReturnsOtherSet()
    {
        // TenantId=2 → should see Beta(2), Delta(4)
        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .WithScopedQueryFilter(sp =>
            {
                var tenant = sp.GetRequiredService<ITenantProvider>();
                return item => item.TenantId == tenant.TenantId;
            })
            .Build(typeof(TestDbContext), opts);

        var sp   = BuildSp(tenantId: 2);
        var ctrl = BuildController(entity, sp);

        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(2, i.TenantId));
    }

    [Fact]
    public async Task GetAsync_ScopedFilter_WithoutServiceProvider_ReturnsAll()
    {
        // When no IServiceProvider is passed to the controller (tests that don't need scoped filter),
        // the scoped filter is skipped entirely — no exception, all records returned.
        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .WithScopedQueryFilter(sp =>
            {
                var tenant = sp.GetRequiredService<ITenantProvider>();
                return item => item.TenantId == tenant.TenantId;
            })
            .Build(typeof(TestDbContext), opts);

        // Pass sp: null → scoped filters skipped
        var ctrl  = BuildController(entity, sp: null);
        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(5, items.Count);
    }

    [Fact]
    public async Task GetAsync_ScopedFilter_StacksWithStaticQueryFilter()
    {
        // Static filter: IsActive. Scoped filter: TenantId==1.
        // Expected: Alpha(1,t1), Epsilon(5,t1) — active AND tenant 1
        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .WithQueryFilter(i => i.IsActive)
            .WithScopedQueryFilter(sp =>
            {
                var tenant = sp.GetRequiredService<ITenantProvider>();
                return i => i.TenantId == tenant.TenantId;
            })
            .Build(typeof(TestDbContext), opts);

        var sp    = BuildSp(tenantId: 1);
        var ctrl  = BuildController(entity, sp);
        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => { Assert.True(i.IsActive); Assert.Equal(1, i.TenantId); });
    }

    [Fact]
    public async Task GetAsync_MultipleScopedFilters_AllApplied_ANDSemantics()
    {
        // Two scoped filters: TenantId==1 AND IsActive.
        // Expected: Alpha(1), Epsilon(5)
        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .WithScopedQueryFilter(_ => i => i.TenantId == 1)
            .WithScopedQueryFilter(_ => i => i.IsActive)
            .Build(typeof(TestDbContext), opts);

        var sp    = BuildSp(tenantId: 1);
        var ctrl  = BuildController(entity, sp);
        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => { Assert.Equal(1, i.TenantId); Assert.True(i.IsActive); });
    }

    [Fact]
    public async Task GetAsync_ById_ScopedFilter_Returns404_WhenFilteredOut()
    {
        // TenantId=1 filter — Beta (Id=2, TenantId=2) must return 404
        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .WithScopedQueryFilter(sp =>
            {
                var tenant = sp.GetRequiredService<ITenantProvider>();
                return i => i.TenantId == tenant.TenantId;
            })
            .Build(typeof(TestDbContext), opts);

        var sp     = BuildSp(tenantId: 1);
        var ctrl   = BuildController(entity, sp);
        var route  = new OtterApiRouteInfo { Entity = entity, Id = "2", IncludeExpression = [] };

        var result = await ctrl.GetAsync(route);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAsync_ById_ScopedFilter_Returns200_WhenPassesFilter()
    {
        // TenantId=1 filter — Alpha (Id=1, TenantId=1) must return 200
        var opts   = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = opts.Entity<TestItem>("/items")
            .WithScopedQueryFilter(sp =>
            {
                var tenant = sp.GetRequiredService<ITenantProvider>();
                return i => i.TenantId == tenant.TenantId;
            })
            .Build(typeof(TestDbContext), opts);

        var sp     = BuildSp(tenantId: 1);
        var ctrl   = BuildController(entity, sp);
        var route  = new OtterApiRouteInfo { Entity = entity, Id = "1", IncludeExpression = [] };

        var result = await ctrl.GetAsync(route);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, ((TestItem)result.Value!).Id);
    }
}

