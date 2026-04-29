using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Moq;
using OtterApi.Configs;
using OtterApi.Controllers;
using OtterApi.Enums;
using OtterApi.Models;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Controllers;

/// <summary>
/// Integration tests for .WithSoftDelete(p => p.IsDeleted).
/// Verifies that DeleteAsync sets the flag instead of calling dbContext.Remove,
/// that the auto-registered query filter hides soft-deleted records from all GET
/// endpoints, and that BeforeSave / AfterSave hooks still fire correctly.
/// </summary>
public class ControllerSoftDeleteIntegrationTests : IDisposable
{
    // Seed:
    //  Id  Name    IsDeleted
    //   1  Alpha   false
    //   2  Beta    false
    //   3  Gamma   true   ← already soft-deleted

    private readonly TestDbContext _db;

    public ControllerSoftDeleteIntegrationTests()
    {
        _db = DbContextFactory.CreateInMemory();
        _db.SoftDeleteItems.AddRange(
            new TestSoftDeleteItem { Id = 1, Name = "Alpha", IsDeleted = false },
            new TestSoftDeleteItem { Id = 2, Name = "Beta",  IsDeleted = false },
            new TestSoftDeleteItem { Id = 3, Name = "Gamma", IsDeleted = true  });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose() => _db.Dispose();

    // ── Builder helpers ───────────────────────────────────────────────────────

    private OtterApiEntity BuildSoftDeleteEntity()
    {
        var options = new OtterApiOptions { Path = "/api" };
        return options.Entity<TestSoftDeleteItem>("/items")
            .WithSoftDelete(x => x.IsDeleted)
            .Build(typeof(TestDbContext), options);
    }

    private OtterApiEntity BuildHardDeleteEntity()
    {
        var options = new OtterApiOptions { Path = "/api" };
        return options.Entity<TestSoftDeleteItem>("/items")
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

    private static OtterApiRouteInfo ByIdRoute(OtterApiEntity entity, string id) => new()
    {
        Entity = entity, Id = id, IncludeExpression = []
    };

    private static OtterApiRouteInfo CollectionRoute(OtterApiEntity entity) => new()
    {
        Entity = entity, IncludeExpression = []
    };

    private static List<TestSoftDeleteItem> Items(ObjectResult result) =>
        ((List<object>)result.Value!).Cast<TestSoftDeleteItem>().ToList();

    // ══════════════════════════════════════════════════════════════════════════
    // DELETE — soft-delete behaviour
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_SoftDelete_Returns204()
    {
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        var result = await ctrl.DeleteAsync(ByIdRoute(entity, "1"));

        Assert.Equal(204, result.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_SetsIsDeletedTrue_RecordRemainsInDb()
    {
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        await ctrl.DeleteAsync(ByIdRoute(entity, "1"));

        var item = await _db.SoftDeleteItems.FindAsync(1);
        Assert.NotNull(item);
        Assert.True(item.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_TotalRowCountUnchanged()
    {
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        await ctrl.DeleteAsync(ByIdRoute(entity, "2"));

        Assert.Equal(3, _db.SoftDeleteItems.Count());
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_Returns404_ForAlreadySoftDeletedRecord()
    {
        // Gamma (Id=3) is already soft-deleted — auto query filter hides it → 404
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        var result = await ctrl.DeleteAsync(ByIdRoute(entity, "3"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_Returns404_ForNonExistentRecord()
    {
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        var result = await ctrl.DeleteAsync(ByIdRoute(entity, "999"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET — auto query filter hides soft-deleted records
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_Collection_HidesSoftDeletedRecords()
    {
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        // Gamma (Id=3, IsDeleted=true) must be excluded
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.False(i.IsDeleted));
    }

    [Fact]
    public async Task GetAsync_ById_Returns404_ForSoftDeletedRecord()
    {
        // Gamma (Id=3) exists in DB but IsDeleted=true → query filter hides it
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        var result = await ctrl.GetAsync(ByIdRoute(entity, "3"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAsync_ById_Returns200_ForNonDeletedRecord()
    {
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        var result = await ctrl.GetAsync(ByIdRoute(entity, "1"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, ((TestSoftDeleteItem)result.Value!).Id);
    }

    [Fact]
    public async Task GetAsync_Count_ExcludesSoftDeletedRecords()
    {
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        var route = CollectionRoute(entity);
        route.IsCount = true;

        var result = await ctrl.GetAsync(route);

        Assert.Equal(2, (int)result.Value!);  // Alpha and Beta only
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET after soft-delete — deleted record disappears from subsequent GETs
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_ById_Returns404_AfterSoftDelete()
    {
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        await ctrl.DeleteAsync(ByIdRoute(entity, "1"));
        _db.ChangeTracker.Clear();

        var result = await ctrl.GetAsync(ByIdRoute(entity, "1"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAsync_Collection_ExcludesRecordAfterSoftDelete()
    {
        var entity = BuildSoftDeleteEntity();
        var ctrl   = BuildController(entity);

        await ctrl.DeleteAsync(ByIdRoute(entity, "1"));
        _db.ChangeTracker.Clear();

        var items = Items(await ctrl.GetAsync(CollectionRoute(entity)));

        Assert.Single(items);
        Assert.Equal("Beta", items[0].Name);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // BeforeSave / AfterSave hooks still fire on soft-delete
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_SoftDelete_BeforeSaveHookFires_WithDeleteOperation()
    {
        var hookFired = false;
        OtterApiCrudOperation? capturedOp = null;

        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestSoftDeleteItem>("/items")
            .WithSoftDelete(x => x.IsDeleted)
            .BeforeSave((ctx, e, orig, op) =>
            {
                hookFired  = true;
                capturedOp = op;
            })
            .Build(typeof(TestDbContext), options);

        await BuildController(entity).DeleteAsync(ByIdRoute(entity, "1"));

        Assert.True(hookFired);
        Assert.Equal(OtterApiCrudOperation.Delete, capturedOp);
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_AfterSaveHookFires_WithDeleteOperation()
    {
        var hookFired = false;
        OtterApiCrudOperation? capturedOp = null;

        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestSoftDeleteItem>("/items")
            .WithSoftDelete(x => x.IsDeleted)
            .AfterSave((ctx, e, orig, op) =>
            {
                hookFired  = true;
                capturedOp = op;
            })
            .Build(typeof(TestDbContext), options);

        await BuildController(entity).DeleteAsync(ByIdRoute(entity, "1"));

        Assert.True(hookFired);
        Assert.Equal(OtterApiCrudOperation.Delete, capturedOp);
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_BeforeSaveHook_ReceivesEntityWithFlagAlreadySet()
    {
        // SoftDeleteSetter runs before PreSaveHandlers, so the hook should see IsDeleted=true.
        bool? isDeletedInHook = null;

        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestSoftDeleteItem>("/items")
            .WithSoftDelete(x => x.IsDeleted)
            .BeforeSave((ctx, e, orig, op) =>
            {
                if (op == OtterApiCrudOperation.Delete)
                    isDeletedInHook = e.IsDeleted;
            })
            .Build(typeof(TestDbContext), options);

        await BuildController(entity).DeleteAsync(ByIdRoute(entity, "1"));

        Assert.True(isDeletedInHook);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Hard-delete regression — without WithSoftDelete, Remove is still called
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_WithoutSoftDelete_HardDeletesRecord()
    {
        var entity = BuildHardDeleteEntity();
        var ctrl   = BuildController(entity);

        var result = await ctrl.DeleteAsync(ByIdRoute(entity, "1"));

        Assert.Equal(204, result.StatusCode);

        var gone = await _db.SoftDeleteItems.FindAsync(1);
        Assert.Null(gone);
    }

    [Fact]
    public async Task DeleteAsync_WithoutSoftDelete_RowCountDecreases()
    {
        var entity = BuildHardDeleteEntity();
        var ctrl   = BuildController(entity);

        await ctrl.DeleteAsync(ByIdRoute(entity, "1"));

        Assert.Equal(2, _db.SoftDeleteItems.Count());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WithSoftDelete builder validation
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithSoftDelete_InvalidExpression_ThrowsArgumentException()
    {
        var options = new OtterApiOptions { Path = "/api" };

        // Constant expression (not a property access) must be rejected at builder time
        Assert.Throws<ArgumentException>(() =>
            options.Entity<TestSoftDeleteItem>("/items")
                .WithSoftDelete(x => true));
    }

    [Fact]
    public void WithSoftDelete_AutoRegistersQueryFilter_AndSetter()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestSoftDeleteItem>("/items")
            .WithSoftDelete(x => x.IsDeleted)
            .Build(typeof(TestDbContext), options);

        Assert.Single(entity.QueryFilters);
        Assert.NotNull(entity.SoftDeleteSetter);
    }

    [Fact]
    public void WithSoftDelete_WithAdditionalQueryFilter_RegistersBothFilters()
    {
        var options = new OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestSoftDeleteItem>("/items")
            .WithSoftDelete(x => x.IsDeleted)
            .WithQueryFilter(x => x.Id > 0)  // extra filter
            .Build(typeof(TestDbContext), options);

        Assert.Equal(2, entity.QueryFilters.Count);
    }
}
