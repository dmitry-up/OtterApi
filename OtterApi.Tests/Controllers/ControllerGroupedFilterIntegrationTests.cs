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
/// Integration tests for the grouped filter syntax introduced to solve the
/// "operator=or is global" problem.
///
/// New syntax:
///   filter[0][Prop]=value &amp; filter[0][Prop2][op]=val &amp; operator[0]=or &amp; filter[1][Prop3]=val
///   → (Prop == value OR Prop2 op val) AND Prop3 == val
///
/// Tests run through OtterApiExpressionBuilder → OtterApiRestController → in-memory EF Core
/// to verify the full parse-build-execute pipeline end-to-end.
///
/// Dataset (5 TestProducts):
///   Id=1  Alpha    Price= 5.00  CategoryId=1
///   Id=2  Beta     Price=15.00  CategoryId=1
///   Id=3  Gamma    Price=25.00  CategoryId=2
///   Id=4  Delta    Price= 8.00  CategoryId=2
///   Id=5  Epsilon  Price=30.00  CategoryId=1
/// </summary>
public class ControllerGroupedFilterIntegrationTests : IDisposable
{
    private readonly TestDbContext          _db;
    private readonly OtterApiRestController _ctrl;
    private readonly OtterApiEntity         _entity;

    public ControllerGroupedFilterIntegrationTests()
    {
        _db = DbContextFactory.CreateInMemory();
        _db.Products.AddRange(
            new TestProduct { Id = 1, Name = "Alpha",   Price =  5.00m, CategoryId = 1 },
            new TestProduct { Id = 2, Name = "Beta",    Price = 15.00m, CategoryId = 1 },
            new TestProduct { Id = 3, Name = "Gamma",   Price = 25.00m, CategoryId = 2 },
            new TestProduct { Id = 4, Name = "Delta",   Price =  8.00m, CategoryId = 2 },
            new TestProduct { Id = 5, Name = "Epsilon", Price = 30.00m, CategoryId = 1 });
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

    /// <summary>
    /// Simulates what OtterApiRequestProcessor does for a real HTTP request:
    /// builds OtterApiRouteInfo from a query-string dictionary via OtterApiExpressionBuilder.
    /// </summary>
    private OtterApiRouteInfo RouteFromQuery(Dictionary<string, string> queryParams)
    {
        var qs = new QueryCollection(
            queryParams.ToDictionary(k => k.Key, k => new StringValues(k.Value)));
        var builder = new OtterApiExpressionBuilder(qs, _entity);
        return new OtterApiRouteInfo
        {
            Entity            = _entity,
            FilterApply       = builder.BuildFilterResult(),
            SortApply         = builder.BuildSortResult(),
            IncludeExpression = [],
        };
    }

    private static List<TestProduct> Items(ObjectResult result) =>
        ((List<object>)result.Value!).Cast<TestProduct>().ToList();

    // ══════════════════════════════════════════════════════════════════════════
    // Single group with OR
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_GroupedFilter_SingleGroupOr_ReturnsUnion()
    {
        // filter[0][Price][gt]=10 & filter[0][CategoryId]=1 & operator[0]=or
        // → Price > 10 OR CategoryId == 1
        // Price>10: Beta(15), Gamma(25), Epsilon(30)
        // CategoryId==1: Alpha, Beta, Epsilon
        // Union = Alpha, Beta, Gamma, Epsilon = 4 items
        var route = RouteFromQuery(new()
        {
            ["filter[0][Price][gt]"]  = "10",
            ["filter[0][CategoryId]"] = "1",
            ["operator[0]"]           = "or"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(4, products.Count);
        Assert.DoesNotContain(products, p => p.Name == "Delta");
    }

    [Fact]
    public async Task GetAsync_GroupedFilter_SingleGroupOr_AllMatch_ReturnsAll()
    {
        // Price > 1 OR Price < 1000 — both conditions cover everything → all 5
        var route = RouteFromQuery(new()
        {
            ["filter[0][Price][gt]"]   = "1",
            ["filter[0][Price][lteq]"] = "1000",
            ["operator[0]"]            = "or"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(5, products.Count);
    }

    [Fact]
    public async Task GetAsync_GroupedFilter_SingleGroupOr_NoMatch_ReturnsEmpty()
    {
        // Name == "NonExistent" OR Price > 9999 → nothing matches
        var route = RouteFromQuery(new()
        {
            ["filter[0][Name]"]       = "NonExistent",
            ["filter[0][Price][gt]"]  = "9999",
            ["operator[0]"]           = "or"
        });

        Assert.Empty(Items(await _ctrl.GetAsync(route)));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Two groups — OR within group 0, AND between groups
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_GroupedFilter_TwoGroups_OrInFirst_AndBetween()
    {
        // (Price > 10 OR CategoryId == 1)  AND  Name contains "a"
        // Group 0 = {Alpha, Beta, Gamma, Epsilon}
        // Group 1: Name.Contains("a") = Alpha(a✓), Beta(a✓), Gamma(a✓), Delta(a✓), Epsilon(no)
        // Intersection of Group0 and Group1: Alpha, Beta, Gamma = 3
        var route = RouteFromQuery(new()
        {
            ["filter[0][Price][gt]"]  = "10",
            ["filter[0][CategoryId]"] = "1",
            ["operator[0]"]           = "or",
            ["filter[1][Name][like]"] = "a"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(3, products.Count);
        Assert.All(products, p => Assert.Contains("a", p.Name));
        Assert.DoesNotContain(products, p => p.Name == "Epsilon");
        Assert.DoesNotContain(products, p => p.Name == "Delta");
    }

    [Fact]
    public async Task GetAsync_GroupedFilter_TwoGroups_AndInBoth_FiltersToIntersection()
    {
        // filter[0][CategoryId]=1 (AND) AND filter[1][Price][gt]=10
        // CategoryId==1: Alpha, Beta, Epsilon
        // Price>10:      Beta, Gamma, Epsilon
        // Intersection:  Beta, Epsilon = 2
        var route = RouteFromQuery(new()
        {
            ["filter[0][CategoryId]"] = "1",
            ["filter[1][Price][gt]"]  = "10"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(2, products.Count);
        var names = products.Select(p => p.Name).ToHashSet();
        Assert.Contains("Beta",    names);
        Assert.Contains("Epsilon", names);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Three groups
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_GroupedFilter_ThreeGroups_AllAnd_NarrowsToSingleItem()
    {
        // filter[0][CategoryId]=1  AND  filter[1][Price][gt]=10  AND  filter[2][Name][like]=a
        // CategoryId==1:        Alpha, Beta, Epsilon
        // Price>10:             Beta, Gamma, Epsilon
        // Name.Contains("a"):   Alpha, Beta, Gamma, Delta
        // All three AND:        Beta only = 1 item
        var route = RouteFromQuery(new()
        {
            ["filter[0][CategoryId]"] = "1",
            ["filter[1][Price][gt]"]  = "10",
            ["filter[2][Name][like]"] = "a"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Single(products);
        Assert.Equal("Beta", products[0].Name);
    }

    [Fact]
    public async Task GetAsync_GroupedFilter_ThreeGroups_OrInAll_ReturnsUnion()
    {
        // Each group has a single filter, each with OR operator
        // (Name==Alpha) OR-within-group1 → just Name==Alpha
        // (Name==Beta)  OR-within-group2 → just Name==Beta
        // (Name==Gamma) OR-within-group3 → just Name==Gamma
        // AND between groups → Name==Alpha AND Name==Beta AND Name==Gamma → empty
        // Wait, OR within a single-item group is same as AND, so this is Alpha AND Beta AND Gamma = 0
        // Better test: OR within each group with two conditions
        // filter[0][CategoryId]=1 & filter[0][Price][gt]=20 & operator[0]=or  → Cat==1 OR Price>20
        // filter[1][Name][like]=a  (AND)
        // filter[2][Id][gt]=2  (AND)
        // Cat==1 OR Price>20: {Alpha,Beta,Epsilon,Gamma} (Price>20: Gamma,Epsilon + Cat==1: Alpha,Beta,Epsilon = union)
        // Name.Contains("a"): Alpha,Beta,Gamma,Delta
        // Id>2: Gamma,Delta,Epsilon
        // All three AND: {Alpha,Beta,Epsilon,Gamma} ∩ {Alpha,Beta,Gamma,Delta} ∩ {Gamma,Delta,Epsilon}
        //   = {Alpha,Beta,Gamma,Epsilon} ∩ {Alpha,Beta,Gamma,Delta} = {Alpha,Beta,Gamma}
        //   ∩ {Gamma,Delta,Epsilon} = {Gamma}
        var route = RouteFromQuery(new()
        {
            ["filter[0][CategoryId]"] = "1",
            ["filter[0][Price][gt]"]  = "20",
            ["operator[0]"]           = "or",
            ["filter[1][Name][like]"] = "a",
            ["filter[2][Id][gt]"]     = "2"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Single(products);
        Assert.Equal("Gamma", products[0].Name);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Mixed flat (legacy) + grouped syntax
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_GroupedFilter_FlatAndGrouped_CanBeMixed()
    {
        // Flat: filter[CategoryId]=1  (→ "default" group, AND)
        // Grouped: filter[0][Price][gt]=10 & filter[0][Name][like]=a & operator[0]=or
        //   → (Price>10 OR Name.Contains("a"))
        // Combined: (Price>10 OR Name.Contains("a")) AND CategoryId==1
        // Price>10 OR Name.Contains("a"):
        //   Price>10: {Beta,Gamma,Epsilon}
        //   Name.Contains("a"): {Alpha,Beta,Gamma,Delta}
        //   union = all 5
        // AND CategoryId==1: {Alpha,Beta,Epsilon}
        // Result: 3 items
        var route = RouteFromQuery(new()
        {
            ["filter[CategoryId]"]    = "1",
            ["filter[0][Price][gt]"]  = "10",
            ["filter[0][Name][like]"] = "a",
            ["operator[0]"]           = "or"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(3, products.Count);
        Assert.All(products, p => Assert.Equal(1, p.CategoryId));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Legacy flat syntax (backward compatibility)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_FlatOrOperator_StillWorks_BackwardCompat()
    {
        // filter[Price][gt]=10 & filter[CategoryId]=2 & operator=or
        // Price>10: {Beta,Gamma,Epsilon} | CategoryId==2: {Gamma,Delta}
        // Union = {Beta,Gamma,Delta,Epsilon} = 4
        var route = RouteFromQuery(new()
        {
            ["filter[Price][gt]"]  = "10",
            ["filter[CategoryId]"] = "2",
            ["operator"]           = "or"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(4, products.Count);
        Assert.DoesNotContain(products, p => p.Name == "Alpha");
    }

    [Fact]
    public async Task GetAsync_FlatAndFilter_StillWorks_BackwardCompat()
    {
        // filter[CategoryId]=1 & filter[Price][gt]=10  (AND by default)
        // CategoryId==1: {Alpha,Beta,Epsilon} AND Price>10: {Beta,Gamma,Epsilon}
        // = {Beta,Epsilon} = 2
        var route = RouteFromQuery(new()
        {
            ["filter[CategoryId]"] = "1",
            ["filter[Price][gt]"]  = "10"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(2, products.Count);
        Assert.All(products, p => Assert.Equal(1, p.CategoryId));
        Assert.All(products, p => Assert.True(p.Price > 10m));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Case-insensitive operator keyword
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("or")]
    [InlineData("OR")]
    [InlineData("Or")]
    public async Task GetAsync_GroupedFilter_OperatorKeyword_IsCaseInsensitive(string op)
    {
        // All casing variants of "or" must produce the same result
        var route = RouteFromQuery(new()
        {
            ["filter[0][Price][gt]"]  = "10",
            ["filter[0][CategoryId]"] = "1",
            ["operator[0]"]           = op
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(4, products.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Grouped filter + count
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_GroupedFilter_Count_ReturnsFilteredCount()
    {
        // (Price > 10 OR CategoryId == 1) → 4 items
        var route = RouteFromQuery(new()
        {
            ["filter[0][Price][gt]"]  = "10",
            ["filter[0][CategoryId]"] = "1",
            ["operator[0]"]           = "or"
        });
        route.IsCount = true;

        var result = await _ctrl.GetAsync(route);

        Assert.Equal(4, (int)result.Value!);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Grouped filter + sort
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_GroupedFilter_WithSort_SortAppliedOnFilteredSubset()
    {
        // (Price > 10 OR CategoryId == 1) sorted by Price asc
        // Matching: Alpha(5), Beta(15), Gamma(25), Epsilon(30)
        // Sorted by Price asc: Alpha(5), Beta(15), Gamma(25), Epsilon(30)
        var route = RouteFromQuery(new()
        {
            ["filter[0][Price][gt]"]  = "10",
            ["filter[0][CategoryId]"] = "1",
            ["operator[0]"]           = "or",
            ["sort[Price]"]           = "asc"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(4, products.Count);
        var prices = products.Select(p => p.Price).ToList();
        Assert.Equal(prices.OrderBy(x => x).ToList(), prices);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Grouped filter + paged result
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_GroupedFilter_PagedResult_TotalReflectsFilteredCount()
    {
        // (Price > 10 OR CategoryId == 1) → 4 items, pageSize=2 → 2 pages
        var route = RouteFromQuery(new()
        {
            ["filter[0][Price][gt]"]  = "10",
            ["filter[0][CategoryId]"] = "1",
            ["operator[0]"]           = "or"
        });
        route.IsPageResult = true;
        route.Take         = 2;
        route.Page         = 1;

        var paged = (OtterApiPagedResult)(await _ctrl.GetAsync(route)).Value!;

        Assert.Equal(4, paged.Total);
        Assert.Equal(2, paged.PageCount);
        Assert.Equal(2, paged.Items.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // in/nin operators inside grouped filter
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_GroupedFilter_InOperator_WithOrGroup()
    {
        // filter[0][CategoryId][in]=[1] OR filter[0][Price][gt]=20
        // CategoryId in [1]: {Alpha,Beta,Epsilon}
        // Price>20: {Gamma,Epsilon}
        // Union: {Alpha,Beta,Gamma,Epsilon} = 4
        var route = RouteFromQuery(new()
        {
            ["filter[0][CategoryId][in]"] = "[1]",
            ["filter[0][Price][gt]"]      = "20",
            ["operator[0]"]               = "or"
        });

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(4, products.Count);
        Assert.DoesNotContain(products, p => p.Name == "Delta");
    }
}

