using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Moq;
using OtterApi.Builders;
using OtterApi.Controllers;
using OtterApi.Models;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Controllers;

/// <summary>
/// Integration tests for navigation property eager loading via ?include=PropertyName.
/// Verifies that related entities are populated on the returned objects.
///
/// Dataset:
///   Categories: Id=1 "Cat-A", Id=2 "Cat-B"
///   Products:   Id=1 Alpha(Cat-A), Id=2 Beta(Cat-A), Id=3 Gamma(Cat-B)
/// </summary>
public class ControllerIncludeIntegrationTests : IDisposable
{
    private readonly TestDbContext          _db;
    private readonly OtterApiRestController _ctrl;
    private readonly OtterApiEntity         _entity;

    public ControllerIncludeIntegrationTests()
    {
        _db = DbContextFactory.CreateInMemory();

        _db.Categories.AddRange(
            new TestCategory { Id = 1, Title = "Cat-A" },
            new TestCategory { Id = 2, Title = "Cat-B" });

        _db.Products.AddRange(
            new TestProduct { Id = 1, Name = "Alpha", Price =  5m, CategoryId = 1 },
            new TestProduct { Id = 2, Name = "Beta",  Price = 15m, CategoryId = 1 },
            new TestProduct { Id = 3, Name = "Gamma", Price = 25m, CategoryId = 2 });

        _db.SaveChanges();

        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        _entity = options.Entity<TestProduct>("/products")
            .Build(typeof(TestDbContext), options);

        var httpCtx   = new DefaultHttpContext();
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var validator = new Mock<IObjectModelValidator>();
        validator.Setup(v => v.Validate(It.IsAny<ActionContext>(),
            It.IsAny<ValidationStateDictionary>(), It.IsAny<string>(), It.IsAny<object>()));
        _ctrl = new OtterApiRestController(_db, actionCtx, validator.Object);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private OtterApiRouteInfo CollectionRoute(List<string>? includes = null) => new()
    {
        Entity            = _entity,
        IncludeExpression = includes ?? [],
    };

    private OtterApiRouteInfo RouteFromQuery(Dictionary<string, string> queryParams)
    {
        var qs = new QueryCollection(
            queryParams.ToDictionary(k => k.Key, k => new StringValues(k.Value)));
        var builder = new OtterApiExpressionBuilder(qs, _entity);
        return new OtterApiRouteInfo
        {
            Entity            = _entity,
            IncludeExpression = builder.BuildIncludeResult(),
        };
    }

    private static List<TestProduct> Items(ObjectResult result) =>
        ((List<object>)result.Value!).Cast<TestProduct>().ToList();

    // ══════════════════════════════════════════════════════════════════════════
    // Include = Category
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_WithIncludeCategory_LoadsNavigationProperty()
    {
        var route    = CollectionRoute(includes: ["Category"]);
        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(3, products.Count);
        Assert.All(products, p => Assert.NotNull(p.Category));
    }

    [Fact]
    public async Task GetAsync_WithIncludeCategory_CorrectCategoryTitleIsLoaded()
    {
        var route    = CollectionRoute(includes: ["Category"]);
        var products = Items(await _ctrl.GetAsync(route));

        var alpha = products.Single(p => p.Name == "Alpha");
        var gamma = products.Single(p => p.Name == "Gamma");

        Assert.Equal("Cat-A", alpha.Category!.Title);
        Assert.Equal("Cat-B", gamma.Category!.Title);
    }

    [Fact]
    public async Task GetAsync_WithoutInclude_CategoryIsNull()
    {
        // No IncludeExpression → navigation property is NOT loaded
        _db.ChangeTracker.Clear();  // clear any previously tracked entities

        var route    = CollectionRoute(includes: []);
        var products = Items(await _ctrl.GetAsync(route));

        // EF Core InMemory auto-fixes up nav properties when entities are tracked;
        // after clearing tracker they should not be set
        Assert.All(products, p => Assert.Null(p.Category));
    }

    [Fact]
    public async Task GetAsync_IncludeViaQueryString_LoadsNavigationProperty()
    {
        // ?include=Category goes through OtterApiExpressionBuilder → IncludeExpression → Include call
        var route    = RouteFromQuery(new() { ["include"] = "Category" });
        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(3, products.Count);
        Assert.All(products, p => Assert.NotNull(p.Category));
    }

    [Fact]
    public async Task GetAsync_IncludeViaQueryString_CaseInsensitive()
    {
        // "category" (lowercase) should resolve to the "Category" navigation property
        var route    = RouteFromQuery(new() { ["include"] = "category" });
        var products = Items(await _ctrl.GetAsync(route));

        Assert.All(products, p => Assert.NotNull(p.Category));
    }

    [Fact]
    public async Task GetAsync_IncludeUnknownProperty_IsIgnored_NoError()
    {
        // Unknown nav property "Tags" is silently dropped by OtterApiIncludeOtterApiExpression
        var route    = RouteFromQuery(new() { ["include"] = "Tags" });
        var products = Items(await _ctrl.GetAsync(route));

        // Should still return all products, just without any include
        Assert.Equal(3, products.Count);
    }

    [Fact]
    public async Task GetAsync_IncludeScalarProperty_IsIgnored()
    {
        // "Name" is not a navigation property → silently ignored
        var route    = RouteFromQuery(new() { ["include"] = "Name" });
        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(3, products.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Include combined with filter
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_IncludeWithFilter_BothApplied()
    {
        // ?include=Category&filter[CategoryId]=1 → only Cat-A products, with Category loaded
        var route = RouteFromQuery(new()
        {
            ["include"]            = "Category",
            ["filter[CategoryId]"] = "1"
        });
        route.FilterApply = q => ((IQueryable<TestProduct>)q).Where(p => p.CategoryId == 1);

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(2, products.Count);
        Assert.All(products, p =>
        {
            Assert.NotNull(p.Category);
            Assert.Equal("Cat-A", p.Category!.Title);
        });
    }

    [Fact]
    public async Task GetAsync_IncludeWithSort_BothApplied()
    {
        // Include + sort by Name desc
        var route    = CollectionRoute(includes: ["Category"]);
        route.SortApply = q => ((IQueryable<TestProduct>)q).OrderByDescending(p => p.Name);

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(3, products.Count);
        Assert.All(products, p => Assert.NotNull(p.Category));

        // Sorted by Name desc: Gamma, Beta, Alpha
        var names = products.Select(p => p.Name).ToList();
        Assert.Equal(["Gamma", "Beta", "Alpha"], names);
    }
}

