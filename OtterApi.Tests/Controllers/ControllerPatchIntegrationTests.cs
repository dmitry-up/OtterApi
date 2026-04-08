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
/// Integration tests for OtterApiRestController.PatchAsync — RFC 7396 JSON Merge Patch.
/// Each test seeds a real in-memory DB, sends a JsonObject patch, and asserts
/// on the final state of the record in the database.
/// </summary>
public class ControllerPatchIntegrationTests : IDisposable
{
    private readonly TestDbContext          _db;
    private readonly OtterApiRestController _ctrl;
    private readonly OtterApiEntity         _entity;

    public ControllerPatchIntegrationTests()
    {
        _db = DbContextFactory.CreateInMemory();

        _db.Products.AddRange(
            new TestProduct { Id = 1, Name = "Alpha",   Price =  5.00m, CategoryId = 1 },
            new TestProduct { Id = 2, Name = "Beta",    Price = 15.00m, CategoryId = 1 },
            new TestProduct { Id = 3, Name = "Gamma",   Price = 25.00m, CategoryId = 2 });

        _db.SaveChanges();

        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        _entity = options.Entity<TestProduct>("/products")
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private OtterApiRouteInfo RouteInfo(string id) => new()
    {
        Entity            = _entity,
        Id                = id,
        IncludeExpression = [],
    };

    private static JsonObject Patch(object obj)
        => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(obj))!.AsObject();

    private TestProduct FromDb(int id)
    {
        _db.ChangeTracker.Clear();
        return _db.Products.Find(id)!;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Basic field updates
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchAsync_SingleField_UpdatesOnlyThatField()
    {
        _db.ChangeTracker.Clear();

        var result = await _ctrl.PatchAsync(RouteInfo("1"), Patch(new { name = "AlphaUpdated" }));

        Assert.IsType<OkObjectResult>(result);

        var product = FromDb(1);
        Assert.Equal("AlphaUpdated", product.Name);
        Assert.Equal(5.00m,          product.Price);     // unchanged
        Assert.Equal(1,              product.CategoryId); // unchanged
    }

    [Fact]
    public async Task PatchAsync_MultipleFields_UpdatesAllSpecifiedFields()
    {
        _db.ChangeTracker.Clear();

        var result = await _ctrl.PatchAsync(RouteInfo("2"),
            Patch(new { name = "BetaV2", price = 99.99m }));

        Assert.IsType<OkObjectResult>(result);

        var product = FromDb(2);
        Assert.Equal("BetaV2", product.Name);
        Assert.Equal(99.99m,   product.Price);
        Assert.Equal(1,        product.CategoryId); // unchanged
    }

    [Fact]
    public async Task PatchAsync_UnchangedFields_RetainOriginalValues()
    {
        _db.ChangeTracker.Clear();

        // Patch only Price — Name and CategoryId must stay as original
        await _ctrl.PatchAsync(RouteInfo("3"), Patch(new { price = 0.01m }));

        var product = FromDb(3);
        Assert.Equal("Gamma", product.Name);
        Assert.Equal(2,       product.CategoryId);
        Assert.Equal(0.01m,   product.Price);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Return value
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchAsync_Returns200Ok_WithUpdatedEntity()
    {
        _db.ChangeTracker.Clear();

        var result = await _ctrl.PatchAsync(RouteInfo("1"), Patch(new { price = 7.77m }));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task PatchAsync_ResponseBody_ContainsFullUpdatedEntity()
    {
        _db.ChangeTracker.Clear();

        var result  = await _ctrl.PatchAsync(RouteInfo("1"), Patch(new { name = "AlphaNew" }));
        var product = (TestProduct)result.Value!;

        Assert.Equal(1,          product.Id);
        Assert.Equal("AlphaNew", product.Name);
        Assert.Equal(5.00m,      product.Price);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 404 — record not found
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchAsync_Returns404_ForNonExistentId()
    {
        var result = await _ctrl.PatchAsync(RouteInfo("999"), Patch(new { name = "Ghost" }));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 400 — missing Id in route
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchAsync_Returns400_WhenIdIsNull()
    {
        var routeInfo = new OtterApiRouteInfo { Entity = _entity, Id = null, IncludeExpression = [] };

        var result = await _ctrl.PatchAsync(routeInfo, Patch(new { name = "x" }));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PatchAsync_Returns400_WhenIdIsEmptyString()
    {
        var routeInfo = new OtterApiRouteInfo { Entity = _entity, Id = "", IncludeExpression = [] };

        var result = await _ctrl.PatchAsync(routeInfo, Patch(new { name = "x" }));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Unknown / navigation properties in patch are silently ignored
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchAsync_IgnoresUnknownProperties_DoesNotCrash()
    {
        _db.ChangeTracker.Clear();

        // "unknownField" doesn't exist on TestProduct
        var result = await _ctrl.PatchAsync(RouteInfo("1"),
            Patch(new { name = "StillAlpha", unknownField = "whatever" }));

        Assert.IsType<OkObjectResult>(result);
        var product = FromDb(1);
        Assert.Equal("StillAlpha", product.Name);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Empty patch — record unchanged, 200 OK
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchAsync_EmptyPatch_Returns200_RecordUnchanged()
    {
        _db.ChangeTracker.Clear();

        var result = await _ctrl.PatchAsync(RouteInfo("2"), new JsonObject());

        Assert.IsType<OkObjectResult>(result);

        var product = FromDb(2);
        Assert.Equal("Beta",  product.Name);
        Assert.Equal(15.00m,  product.Price);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Hooks: PreSaveHandler and PostSaveHandler are called with Patch operation
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchAsync_PreSaveHandler_IsCalledWithPatchOperation()
    {
        OtterApi.Enums.OtterApiCrudOperation? capturedOp = null;
        object? capturedOriginal = null;

        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = options.Entity<TestProduct>("/products")
            .BeforeSave((ctx, entity, original, op) =>
            {
                capturedOp       = op;
                capturedOriginal = original;
            })
            .Build(typeof(TestDbContext), options);

        var httpCtx   = new DefaultHttpContext();
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var validator = new Mock<IObjectModelValidator>();
        validator.Setup(v => v.Validate(It.IsAny<ActionContext>(),
            It.IsAny<ValidationStateDictionary>(), It.IsAny<string>(), It.IsAny<object>()));

        using var db = DbContextFactory.CreateInMemory();
        db.Products.Add(new TestProduct { Id = 10, Name = "Hook", Price = 1m, CategoryId = 1 });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var ctrl      = new OtterApiRestController(db, actionCtx, validator.Object);
        var routeInfo = new OtterApiRouteInfo { Entity = entity, Id = "10", IncludeExpression = [] };

        await ctrl.PatchAsync(routeInfo, Patch(new { name = "HookUpdated" }));

        Assert.Equal(OtterApi.Enums.OtterApiCrudOperation.Patch, capturedOp);
        Assert.NotNull(capturedOriginal);                              // original snapshot passed
        Assert.Equal("Hook", ((TestProduct)capturedOriginal).Name);   // original, not patched value
    }

    [Fact]
    public async Task PatchAsync_PostSaveHandler_IsCalledAfterSave()
    {
        bool postSaveCalled = false;

        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity = options.Entity<TestProduct>("/products")
            .AfterSave((ctx, entity, original, op) => { postSaveCalled = true; })
            .Build(typeof(TestDbContext), options);

        var httpCtx   = new DefaultHttpContext();
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var validator = new Mock<IObjectModelValidator>();
        validator.Setup(v => v.Validate(It.IsAny<ActionContext>(),
            It.IsAny<ValidationStateDictionary>(), It.IsAny<string>(), It.IsAny<object>()));

        using var db = DbContextFactory.CreateInMemory();
        db.Products.Add(new TestProduct { Id = 20, Name = "PostHook", Price = 1m, CategoryId = 1 });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var ctrl      = new OtterApiRestController(db, actionCtx, validator.Object);
        var routeInfo = new OtterApiRouteInfo { Entity = entity, Id = "20", IncludeExpression = [] };

        await ctrl.PatchAsync(routeInfo, Patch(new { price = 2m }));

        Assert.True(postSaveCalled);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // OtterApiCrudOperation.Patch is in All and is a distinct flag
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CrudOperation_Patch_IsIncludedInAll()
    {
        Assert.True(OtterApi.Enums.OtterApiCrudOperation.All
            .HasFlag(OtterApi.Enums.OtterApiCrudOperation.Patch));
    }

    [Fact]
    public void CrudOperation_Patch_IsDistinctFromOtherFlags()
    {
        var patch = OtterApi.Enums.OtterApiCrudOperation.Patch;

        Assert.False(patch.HasFlag(OtterApi.Enums.OtterApiCrudOperation.Get));
        Assert.False(patch.HasFlag(OtterApi.Enums.OtterApiCrudOperation.Post));
        Assert.False(patch.HasFlag(OtterApi.Enums.OtterApiCrudOperation.Put));
        Assert.False(patch.HasFlag(OtterApi.Enums.OtterApiCrudOperation.Delete));
    }

    [Fact]
    public void Allow_CanExcludePatch_WhileKeepingOtherOperations()
    {
        var ops = OtterApi.Enums.OtterApiCrudOperation.Get
                  | OtterApi.Enums.OtterApiCrudOperation.Post
                  | OtterApi.Enums.OtterApiCrudOperation.Put
                  | OtterApi.Enums.OtterApiCrudOperation.Delete;

        Assert.False(ops.HasFlag(OtterApi.Enums.OtterApiCrudOperation.Patch));
        Assert.True(ops.HasFlag(OtterApi.Enums.OtterApiCrudOperation.Put));
    }
}

