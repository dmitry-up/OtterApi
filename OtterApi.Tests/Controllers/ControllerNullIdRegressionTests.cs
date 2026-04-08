using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using OtterApi.Controllers;
using OtterApi.Models;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Controllers;

/// <summary>
/// Regression tests for OtterApiRestController: guards when routeInfo.Id is null
/// for operations that require an Id (PUT, DELETE).
/// </summary>
public class ControllerNullIdRegressionTests
{
    private static (OtterApiRestController, OtterApiEntity) BuildController(DbContext db)
    {
        var httpContext  = new DefaultHttpContext();
        var actionCtx    = new ActionContext(httpContext, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var validator    = new Mock<IObjectModelValidator>();
        validator.Setup(v => v.Validate(It.IsAny<ActionContext>(), It.IsAny<ValidationStateDictionary>(),
            It.IsAny<string>(), It.IsAny<object>()));

        var options = new OtterApi.Configs.OtterApiOptions { Path = "/api" };
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        var controller = new OtterApiRestController(db, actionCtx, validator.Object);
        return (controller, entity);
    }

    private static OtterApiRouteInfo RouteInfo(OtterApiEntity entity, string? id) =>
        new()
        {
            Entity          = entity,
            Id              = id,
            IncludeExpression = [],
        };

    // ── Bug C: DELETE without id must return 400 BadRequest, not throw ────────

    [Fact]
    public async Task DeleteAsync_WhenIdIsNull_ReturnsBadRequest()
    {
        using var db = DbContextFactory.CreateInMemory();
        var (controller, entity) = BuildController(db);

        var routeInfo = RouteInfo(entity, null);   // no id → simulates DELETE /api/products

        var result = await controller.DeleteAsync(routeInfo);

        Assert.IsAssignableFrom<BadRequestObjectResult>(result);
    }

    // ── Bug C: PUT without id must return 400 BadRequest, not throw ──────────

    [Fact]
    public async Task PutAsync_WhenIdIsNull_ReturnsBadRequest()
    {
        using var db = DbContextFactory.CreateInMemory();
        var (controller, entity) = BuildController(db);

        var routeInfo = RouteInfo(entity, null);  // no id → simulates PUT /api/products
        var product   = new TestProduct { Id = 1, Name = "Test" };

        var result = await controller.PutAsync(routeInfo, product);

        Assert.IsAssignableFrom<BadRequestObjectResult>(result);
    }

    // ── Sanity: DELETE with valid id still works (returns 404 since db empty) ─

    [Fact]
    public async Task DeleteAsync_WhenIdPresent_ReturnsNotFound_ForMissingEntity()
    {
        using var db = DbContextFactory.CreateInMemory();
        var (controller, entity) = BuildController(db);

        var routeInfo = RouteInfo(entity, "999");

        var result = await controller.DeleteAsync(routeInfo);

        Assert.IsAssignableFrom<NotFoundObjectResult>(result);
    }

    // ── Sanity: PUT with valid id still works (returns 404 since db empty) ───

    [Fact]
    public async Task PutAsync_WhenIdPresent_ReturnsNotFound_ForMissingEntity()
    {
        using var db = DbContextFactory.CreateInMemory();
        var (controller, entity) = BuildController(db);

        var routeInfo = RouteInfo(entity, "999");
        var product   = new TestProduct { Id = 999, Name = "Test" };

        var result = await controller.PutAsync(routeInfo, product);

        Assert.IsAssignableFrom<NotFoundObjectResult>(result);
    }
}

