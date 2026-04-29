using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using OtterApi.Configs;
using OtterApi.Controllers;
using OtterApi.Models;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Controllers;

/// <summary>
/// Integration tests for .OnDuplicate(finder) — find-or-create / idempotent POST semantics.
/// Verifies that PostAsync returns the existing entity (200 OK) when the finder returns non-null,
/// and proceeds normally (201 Created) when the finder returns null or is not configured.
/// </summary>
public class ControllerOnDuplicateTests : IDisposable
{
    // Seed: Id=1 "Alpha", Id=2 "Beta"
    private readonly TestDbContext _db;

    public ControllerOnDuplicateTests()
    {
        _db = DbContextFactory.CreateInMemory();
        _db.Products.AddRange(
            new TestProduct { Id = 1, Name = "Alpha", Price = 10m },
            new TestProduct { Id = 2, Name = "Beta",  Price = 20m });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private OtterApiRestController BuildController(OtterApiEntity entity)
    {
        var httpCtx   = new DefaultHttpContext();
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var validator = new Mock<IObjectModelValidator>();
        validator.Setup(v => v.Validate(It.IsAny<ActionContext>(),
            It.IsAny<ValidationStateDictionary>(), It.IsAny<string>(), It.IsAny<object>()));
        return new OtterApiRestController(_db, actionCtx, validator.Object);
    }

    private static OtterApiRouteInfo PostRoute(OtterApiEntity entity) => new()
    {
        Entity = entity, IncludeExpression = []
    };

    // ══════════════════════════════════════════════════════════════════════════════
    // Duplicate found — return existing entity, skip insert
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostAsync_OnDuplicate_Returns200_WhenDuplicateFound()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) =>
                ctx.Set<TestProduct>().FirstOrDefaultAsync(x => x.Name == p.Name))
            .Build(typeof(TestDbContext), options);

        var incoming = new TestProduct { Name = "Alpha", Price = 99m };
        var result   = await BuildController(entity).PostAsync(PostRoute(entity), incoming);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public async Task PostAsync_OnDuplicate_ReturnsExistingEntity_WhenDuplicateFound()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) =>
                ctx.Set<TestProduct>().FirstOrDefaultAsync(x => x.Name == p.Name))
            .Build(typeof(TestDbContext), options);

        var incoming = new TestProduct { Name = "Alpha", Price = 99m };
        var result   = await BuildController(entity).PostAsync(PostRoute(entity), incoming);

        var returned = (TestProduct)result.Value!;
        Assert.Equal(1,      returned.Id);
        Assert.Equal("Alpha", returned.Name);
        Assert.Equal(10m,    returned.Price); // original price, not the incoming 99m
    }

    [Fact]
    public async Task PostAsync_OnDuplicate_DoesNotInsertRow_WhenDuplicateFound()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) =>
                ctx.Set<TestProduct>().FirstOrDefaultAsync(x => x.Name == p.Name))
            .Build(typeof(TestDbContext), options);

        var countBefore = _db.Products.Count();
        await BuildController(entity).PostAsync(PostRoute(entity), new TestProduct { Name = "Alpha" });

        Assert.Equal(countBefore, _db.Products.Count());
    }

    [Fact]
    public async Task PostAsync_OnDuplicate_DoesNotFirePreSaveHook_WhenDuplicateFound()
    {
        bool hookFired = false;
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) =>
                ctx.Set<TestProduct>().FirstOrDefaultAsync(x => x.Name == p.Name))
            .BeforeSave((ctx, p, orig, op) => { hookFired = true; })
            .Build(typeof(TestDbContext), options);

        await BuildController(entity).PostAsync(PostRoute(entity), new TestProduct { Name = "Alpha" });

        Assert.False(hookFired);
    }

    [Fact]
    public async Task PostAsync_OnDuplicate_DoesNotFireAfterSaveHook_WhenDuplicateFound()
    {
        bool hookFired = false;
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) =>
                ctx.Set<TestProduct>().FirstOrDefaultAsync(x => x.Name == p.Name))
            .AfterSave((ctx, p, orig, op) => { hookFired = true; })
            .Build(typeof(TestDbContext), options);

        await BuildController(entity).PostAsync(PostRoute(entity), new TestProduct { Name = "Alpha" });

        Assert.False(hookFired);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // No duplicate — normal 201 Created flow
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostAsync_OnDuplicate_Returns201_WhenNoDuplicate()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) =>
                ctx.Set<TestProduct>().FirstOrDefaultAsync(x => x.Name == p.Name))
            .Build(typeof(TestDbContext), options);

        var incoming = new TestProduct { Name = "Gamma", Price = 30m };
        var result   = await BuildController(entity).PostAsync(PostRoute(entity), incoming);

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
    }

    [Fact]
    public async Task PostAsync_OnDuplicate_InsertsRow_WhenNoDuplicate()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) =>
                ctx.Set<TestProduct>().FirstOrDefaultAsync(x => x.Name == p.Name))
            .Build(typeof(TestDbContext), options);

        var countBefore = _db.Products.Count();
        await BuildController(entity).PostAsync(PostRoute(entity), new TestProduct { Name = "Gamma" });

        Assert.Equal(countBefore + 1, _db.Products.Count());
    }

    [Fact]
    public async Task PostAsync_OnDuplicate_FiresPreSaveHook_WhenNoDuplicate()
    {
        bool hookFired = false;
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) =>
                ctx.Set<TestProduct>().FirstOrDefaultAsync(x => x.Name == p.Name))
            .BeforeSave((ctx, p, orig, op) => { hookFired = true; })
            .Build(typeof(TestDbContext), options);

        await BuildController(entity).PostAsync(PostRoute(entity), new TestProduct { Name = "Gamma" });

        Assert.True(hookFired);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // No OnDuplicate configured — regression: normal 201 Created always
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostAsync_WithoutOnDuplicate_Returns201_Always()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .Build(typeof(TestDbContext), options);

        var result = await BuildController(entity).PostAsync(PostRoute(entity), new TestProduct { Name = "Alpha" });

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Sync overload
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostAsync_OnDuplicate_SyncOverload_Returns200_WhenDuplicateFound()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) => ctx.Set<TestProduct>().FirstOrDefault(x => x.Name == p.Name))
            .Build(typeof(TestDbContext), options);

        var result = await BuildController(entity).PostAsync(PostRoute(entity), new TestProduct { Name = "Alpha" });

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public async Task PostAsync_OnDuplicate_SyncOverload_Returns201_WhenNoDuplicate()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate((ctx, p) => ctx.Set<TestProduct>().FirstOrDefault(x => x.Name == p.Name))
            .Build(typeof(TestDbContext), options);

        var result = await BuildController(entity).PostAsync(PostRoute(entity), new TestProduct { Name = "Gamma" });

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Complex async logic in the finder
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostAsync_OnDuplicate_ComplexAsyncLogic_WorksCorrectly()
    {
        // Finder: find last product in same price range (within ±5), compare names
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products")
            .OnDuplicate(async (ctx, incoming) =>
            {
                var candidates = await ctx.Set<TestProduct>()
                    .Where(p => p.Price >= incoming.Price - 5m && p.Price <= incoming.Price + 5m)
                    .OrderByDescending(p => p.Id)
                    .ToListAsync();

                return candidates.FirstOrDefault(c =>
                    string.Equals(c.Name, incoming.Name, StringComparison.OrdinalIgnoreCase));
            })
            .Build(typeof(TestDbContext), options);

        // "alpha" matches "Alpha" (same price range 10m ±5) → duplicate
        var result1 = await BuildController(entity).PostAsync(PostRoute(entity),
            new TestProduct { Name = "alpha", Price = 10m });
        Assert.Equal(StatusCodes.Status200OK, result1.StatusCode);

        // Different name in same price range → no duplicate
        var result2 = await BuildController(entity).PostAsync(PostRoute(entity),
            new TestProduct { Name = "Delta", Price = 10m });
        Assert.Equal(StatusCodes.Status201Created, result2.StatusCode);
    }
}
