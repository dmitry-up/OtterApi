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
/// Integration tests: seed an in-memory DB, call OtterApiRestController.GetAsync,
/// assert on actual returned data — items, order, counts, pages, filters.
/// </summary>
public class ControllerGetAsyncIntegrationTests : IDisposable
{
    // ── Fixed dataset ─────────────────────────────────────────────────────────
    //  Id  Name      Price  CategoryId
    //   1  Alpha      5.00       1
    //   2  Beta      15.00       1
    //   3  Gamma     25.00       2
    //   4  Delta      8.00       2
    //   5  Epsilon   30.00       1

    private readonly TestDbContext      _db;
    private readonly OtterApiRestController _ctrl;
    private readonly OtterApiEntity     _entity;

    public ControllerGetAsyncIntegrationTests()
    {
        _db = DbContextFactory.CreateInMemory();

        _db.Categories.AddRange(
            new TestCategory { Id = 1, Title = "Cat-A" },
            new TestCategory { Id = 2, Title = "Cat-B" });

        _db.Products.AddRange(
            new TestProduct { Id = 1, Name = "Alpha",   Price =  5.00m, CategoryId = 1 },
            new TestProduct { Id = 2, Name = "Beta",    Price = 15.00m, CategoryId = 1 },
            new TestProduct { Id = 3, Name = "Gamma",   Price = 25.00m, CategoryId = 2 },
            new TestProduct { Id = 4, Name = "Delta",   Price =  8.00m, CategoryId = 2 },
            new TestProduct { Id = 5, Name = "Epsilon", Price = 30.00m, CategoryId = 1 });

        _db.SaveChanges();

        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        _entity = options.Entity<TestProduct>("/products").ExposePagedResult()
            .Build(typeof(TestDbContext), options);

        var httpCtx   = new DefaultHttpContext();
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var validator = new Mock<IObjectModelValidator>();
        validator.Setup(v => v.Validate(
            It.IsAny<ActionContext>(),
            It.IsAny<ValidationStateDictionary>(),
            It.IsAny<string>(),
            It.IsAny<object>()));

        _ctrl = new OtterApiRestController(_db, actionCtx, validator.Object);
    }

    public void Dispose() => _db.Dispose();

    // ── Helper: build a RouteInfo for collection requests ────────────────────

    private OtterApiRouteInfo CollectionRoute(
        string? filterExpression  = null,
        object[]? filterValues    = null,
        string? sortExpression    = null,
        int skip = 0, int take = 0, int page = 0,
        bool isCount = false, bool isPageResult = false,
        List<string>? includes = null) => new()
    {
        Entity           = _entity,
        FilterExpression = filterExpression,
        FilterValues     = filterValues,
        SortExpression   = sortExpression,
        Skip             = skip,
        Take             = take,
        Page             = page,
        IsCount          = isCount,
        IsPageResult     = isPageResult,
        IncludeExpression = includes ?? [],
    };

    private static List<TestProduct> Items(ObjectResult result) =>
        ((List<object>)result.Value!).Cast<TestProduct>().ToList();

    // ══════════════════════════════════════════════════════════════════════════
    // GET all (no filter / sort / pagination)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_NoParams_ReturnsAllItems()
    {
        var result = await _ctrl.GetAsync(CollectionRoute());

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(5, Items(result).Count);
    }

    [Fact]
    public async Task GetAsync_NoSort_DefaultsToIdDescending()
    {
        var result = await _ctrl.GetAsync(CollectionRoute());
        var ids    = Items(result).Select(p => p.Id).ToList();

        // Default sort: Id DESC → [5, 4, 3, 2, 1]
        Assert.Equal([5, 4, 3, 2, 1], ids);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET by Id
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_ById_ReturnsCorrectItem()
    {
        var routeInfo = CollectionRoute();
        routeInfo.Id  = "3";

        var result  = await _ctrl.GetAsync(routeInfo);
        var product = (TestProduct)result.Value!;

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(3, product.Id);
        Assert.Equal("Gamma", product.Name);
    }

    [Fact]
    public async Task GetAsync_ById_ReturnsNotFound_ForMissingItem()
    {
        var routeInfo = CollectionRoute();
        routeInfo.Id  = "999";

        var result = await _ctrl.GetAsync(routeInfo);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET with equality filter (filter[Name]=Alpha)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_EqualityFilter_OnName_ReturnsSingleMatch()
    {
        var route  = CollectionRoute(
            filterExpression: "Name == @0",
            filterValues:     ["Alpha"]);

        var result   = await _ctrl.GetAsync(route);
        var products = Items(result);

        Assert.Single(products);
        Assert.Equal("Alpha", products[0].Name);
    }

    [Fact]
    public async Task GetAsync_EqualityFilter_NoMatch_ReturnsEmpty()
    {
        var route = CollectionRoute(
            filterExpression: "Name == @0",
            filterValues:     ["NonExistent"]);

        var result = await _ctrl.GetAsync(route);

        Assert.Empty(Items(result));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET with operator filter
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_Filter_Gt_OnPrice_ReturnsExpectedItems()
    {
        // Price > 10  →  Beta(15), Gamma(25), Epsilon(30)
        var route = CollectionRoute(
            filterExpression: "Price > @0",
            filterValues:     [10m]);

        var result   = await _ctrl.GetAsync(route);
        var products = Items(result);

        Assert.Equal(3, products.Count);
        Assert.All(products, p => Assert.True(p.Price > 10m));
    }

    [Fact]
    public async Task GetAsync_Filter_Lt_OnPrice_ReturnsExpectedItems()
    {
        // Price < 10  →  Alpha(5), Delta(8)
        var route = CollectionRoute(
            filterExpression: "Price < @0",
            filterValues:     [10m]);

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(2, products.Count);
        Assert.All(products, p => Assert.True(p.Price < 10m));
    }

    [Fact]
    public async Task GetAsync_Filter_Like_OnName_ReturnsPartialMatches()
    {
        // Name.Contains("a")  →  Alpha, Gamma, Delta, Epsilon (case-sensitive in memory)
        var route = CollectionRoute(
            filterExpression: "Name.Contains(@0)",
            filterValues:     ["a"]);

        var products = Items(await _ctrl.GetAsync(route));

        Assert.All(products, p => Assert.Contains("a", p.Name));
    }

    [Fact]
    public async Task GetAsync_Filter_In_OnCategoryId_ReturnsItemsInSet()
    {
        // CategoryId in [1]  →  Alpha, Beta, Epsilon
        var route = CollectionRoute(
            filterExpression: "@0.Contains(CategoryId)",
            filterValues:     [new List<int> { 1 }]);

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(3, products.Count);
        Assert.All(products, p => Assert.Equal(1, p.CategoryId));
    }

    [Fact]
    public async Task GetAsync_Filter_Nin_OnCategoryId_ReturnsItemsNotInSet()
    {
        // CategoryId not-in [1]  →  Gamma, Delta
        var route = CollectionRoute(
            filterExpression: "!@0.Contains(CategoryId)",
            filterValues:     [new List<int> { 1 }]);

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(2, products.Count);
        Assert.All(products, p => Assert.NotEqual(1, p.CategoryId));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET with AND / OR combined filters
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_AndFilter_TwoConditions_ReturnsIntersection()
    {
        // Price > 5 AND CategoryId == 1  →  Beta(15,1), Epsilon(30,1)
        var route = CollectionRoute(
            filterExpression: "Price > @0 && CategoryId == @1",
            filterValues:     [5m, 1]);

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(2, products.Count);
        Assert.All(products, p =>
        {
            Assert.True(p.Price > 5m);
            Assert.Equal(1, p.CategoryId);
        });
    }

    [Fact]
    public async Task GetAsync_OrFilter_TwoConditions_ReturnsUnion()
    {
        // Name == "Alpha" OR Name == "Gamma"  →  2 items
        var route = CollectionRoute(
            filterExpression: "Name == @0 || Name == @1",
            filterValues:     ["Alpha", "Gamma"]);

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(2, products.Count);
        var names = products.Select(p => p.Name).ToHashSet();
        Assert.Contains("Alpha", names);
        Assert.Contains("Gamma", names);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET with sort
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_SortByName_Ascending_ReturnsAlphabeticalOrder()
    {
        var route = CollectionRoute(sortExpression: "Name asc");

        var names = Items(await _ctrl.GetAsync(route)).Select(p => p.Name).ToList();

        Assert.Equal(["Alpha", "Beta", "Delta", "Epsilon", "Gamma"], names);
    }

    [Fact]
    public async Task GetAsync_SortByName_Descending_ReturnsReverseAlphabeticalOrder()
    {
        var route = CollectionRoute(sortExpression: "Name desc");

        var names = Items(await _ctrl.GetAsync(route)).Select(p => p.Name).ToList();

        Assert.Equal(["Gamma", "Epsilon", "Delta", "Beta", "Alpha"], names);
    }

    [Fact]
    public async Task GetAsync_SortByPrice_Ascending_ReturnsCheapestFirst()
    {
        var route = CollectionRoute(sortExpression: "Price asc");

        var prices = Items(await _ctrl.GetAsync(route)).Select(p => p.Price).ToList();

        Assert.Equal(prices.OrderBy(x => x).ToList(), prices);
    }

    [Fact]
    public async Task GetAsync_SortByPrice_Descending_ReturnsMostExpensiveFirst()
    {
        var route = CollectionRoute(sortExpression: "Price desc");

        var prices = Items(await _ctrl.GetAsync(route)).Select(p => p.Price).ToList();

        Assert.Equal(prices.OrderByDescending(x => x).ToList(), prices);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET with Skip/Take (pagination without paged-result wrapper)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_SkipTake_ReturnsCorrectPage()
    {
        // Sort by Id asc, take page 2 (skip 2, take 2) → Ids 3,4
        var route = CollectionRoute(
            sortExpression: "Id asc",
            skip: 2, take: 2);

        var ids = Items(await _ctrl.GetAsync(route)).Select(p => p.Id).ToList();

        Assert.Equal([3, 4], ids);
    }

    [Fact]
    public async Task GetAsync_SkipTake_FirstPage_ReturnsFirstItems()
    {
        var route = CollectionRoute(
            sortExpression: "Id asc",
            skip: 0, take: 3);

        var ids = Items(await _ctrl.GetAsync(route)).Select(p => p.Id).ToList();

        Assert.Equal([1, 2, 3], ids);
    }

    [Fact]
    public async Task GetAsync_SkipTake_LastPage_ReturnsRemainingItems()
    {
        var route = CollectionRoute(
            sortExpression: "Id asc",
            skip: 4, take: 10);

        var ids = Items(await _ctrl.GetAsync(route)).Select(p => p.Id).ToList();

        Assert.Equal([5], ids);
    }

    [Fact]
    public async Task GetAsync_Take0_ReturnsAllItems()
    {
        // Take == 0 means no pagination → return everything
        var route = CollectionRoute(sortExpression: "Id asc", take: 0);

        var result = Items(await _ctrl.GetAsync(route));

        Assert.Equal(5, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET /count
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_IsCount_ReturnsTotal()
    {
        var route = CollectionRoute(isCount: true);

        var result = await _ctrl.GetAsync(route);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(5, (int)result.Value!);
    }

    [Fact]
    public async Task GetAsync_IsCount_WithFilter_ReturnsFilteredCount()
    {
        // Count where Price > 10 → 3
        var route = CollectionRoute(
            filterExpression: "Price > @0",
            filterValues:     [10m],
            isCount:          true);

        var result = await _ctrl.GetAsync(route);

        Assert.Equal(3, (int)result.Value!);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET /pagedresult
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_IsPageResult_ReturnsPagedResultWrapper()
    {
        var route = CollectionRoute(isPageResult: true, take: 2, page: 1);

        var result = await _ctrl.GetAsync(route);

        Assert.IsType<OkObjectResult>(result);
        Assert.IsType<OtterApiPagedResult>(result.Value);
    }

    [Fact]
    public async Task GetAsync_IsPageResult_Page1_ReturnsFirstTwoItems()
    {
        // Sort by Id asc, page=1, pageSize=2 → Ids [5,4] (desc default if no sort set)
        // But we set sortExpression explicitly
        var route = CollectionRoute(
            sortExpression: "Id asc",
            isPageResult:   true,
            take:           2,
            page:           1);

        var paged = (OtterApiPagedResult)(await _ctrl.GetAsync(route)).Value!;

        Assert.Equal(1, paged.Page);
        Assert.Equal(2, paged.PageSize);
        Assert.Equal(5, paged.Total);
        Assert.Equal(3, paged.PageCount);  // ceil(5/2)
        Assert.Equal(2, paged.Items.Count);

        var ids = paged.Items.Cast<TestProduct>().Select(p => p.Id).ToList();
        Assert.Equal([1, 2], ids);
    }

    [Fact]
    public async Task GetAsync_IsPageResult_Page2_ReturnsSecondPage()
    {
        var route = CollectionRoute(
            sortExpression: "Id asc",
            isPageResult:   true,
            take:           2,
            page:           2);

        var paged = (OtterApiPagedResult)(await _ctrl.GetAsync(route)).Value!;

        Assert.Equal(2, paged.Page);
        var ids = paged.Items.Cast<TestProduct>().Select(p => p.Id).ToList();
        Assert.Equal([3, 4], ids);
    }

    [Fact]
    public async Task GetAsync_IsPageResult_LastPage_HasCorrectItemCount()
    {
        var route = CollectionRoute(
            sortExpression: "Id asc",
            isPageResult:   true,
            take:           2,
            page:           3);

        var paged = (OtterApiPagedResult)(await _ctrl.GetAsync(route)).Value!;

        Assert.Equal(3, paged.Page);
        Assert.Single(paged.Items);  // only item Id=5 on last page
    }

    [Fact]
    public async Task GetAsync_IsPageResult_DefaultPageSize_Uses10()
    {
        // take == 0 → controller defaults to pageSize = 10
        var route = CollectionRoute(isPageResult: true, take: 0, page: 1);

        var paged = (OtterApiPagedResult)(await _ctrl.GetAsync(route)).Value!;

        Assert.Equal(10, paged.PageSize);
        Assert.Equal(5, paged.Items.Count);  // all 5 items fit in pageSize 10
    }

    [Fact]
    public async Task GetAsync_IsPageResult_DefaultPage_Uses1()
    {
        // page < 1 → controller uses page = 1
        var route = CollectionRoute(isPageResult: true, take: 2, page: 0);

        var paged = (OtterApiPagedResult)(await _ctrl.GetAsync(route)).Value!;

        Assert.Equal(1, paged.Page);
    }

    [Fact]
    public async Task GetAsync_IsPageResult_WithFilter_ReturnsFilteredTotal()
    {
        // Price > 10 → 3 items, pageSize=2 → 2 pages
        var route = CollectionRoute(
            filterExpression: "Price > @0",
            filterValues:     [10m],
            isPageResult:     true,
            take:             2,
            page:             1);

        var paged = (OtterApiPagedResult)(await _ctrl.GetAsync(route)).Value!;

        Assert.Equal(3, paged.Total);
        Assert.Equal(2, paged.PageCount);
        Assert.Equal(2, paged.Items.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Combined: filter + sort + pagination
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_FilterAndSort_ReturnsCorrectOrderedSubset()
    {
        // CategoryId == 1 (Alpha, Beta, Epsilon), sorted by Price desc → Epsilon, Beta, Alpha
        var route = CollectionRoute(
            filterExpression: "CategoryId == @0",
            filterValues:     [1],
            sortExpression:   "Price desc");

        var products = Items(await _ctrl.GetAsync(route));

        Assert.Equal(3, products.Count);
        Assert.Equal(["Epsilon", "Beta", "Alpha"], products.Select(p => p.Name).ToList());
    }

    [Fact]
    public async Task GetAsync_FilterAndPaging_ReturnsCorrectPage()
    {
        // Price > 5 (Beta,Gamma,Delta,Epsilon = 4 items), sorted by Id asc, page1/size2 → Beta,Gamma
        var route = CollectionRoute(
            filterExpression: "Price > @0",
            filterValues:     [5m],
            sortExpression:   "Id asc",
            skip:             0,
            take:             2);

        var ids = Items(await _ctrl.GetAsync(route)).Select(p => p.Id).ToList();

        Assert.Equal([2, 3], ids);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // POST / PUT / DELETE sanity (data-level, not HTTP layer)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostAsync_AddsNewItem_ReturnsCreated()
    {
        var routeInfo = CollectionRoute();
        var newProduct = new TestProduct { Id = 10, Name = "New", Price = 99m, CategoryId = 1 };

        var result = await _ctrl.PostAsync(routeInfo, newProduct);

        Assert.IsType<CreatedResult>(result);
        Assert.NotNull(_db.Products.Find(10));
    }

    [Fact]
    public async Task DeleteAsync_RemovesItem_ReturnsOk()
    {
        var routeInfo = CollectionRoute();
        routeInfo.Id  = "1";

        var result = await _ctrl.DeleteAsync(routeInfo);

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(_db.Products.Find(1));
    }

    [Fact]
    public async Task PutAsync_UpdatesItem_ReturnsOk()
    {
        var routeInfo = CollectionRoute();
        routeInfo.Id  = "2";

        var updated = new TestProduct { Id = 2, Name = "BetaUpdated", Price = 99m, CategoryId = 1 };

        // Clear the change tracker so the seeded entity is not tracked alongside the update
        _db.ChangeTracker.Clear();

        var result = await _ctrl.PutAsync(routeInfo, updated);

        Assert.IsType<OkObjectResult>(result);

        _db.ChangeTracker.Clear();
        var fromDb = _db.Products.Find(2)!;
        Assert.Equal("BetaUpdated", fromDb.Name);
        Assert.Equal(99m,           fromDb.Price);
    }
}


