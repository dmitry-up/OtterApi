using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using OtterApi.Configs;
using OtterApi.Enums;
using OtterApi.Tests.Helpers;
using Xunit;

namespace OtterApi.Tests.Builders;

/// <summary>
/// Contract: OtterApiEntityBuilder.Build() produces a correctly populated
/// OtterApiEntity based on the DbContext type and configured options.
/// </summary>
public class EntityBuilderTests
{
    private static OtterApiOptions OptionsWithPath(string path = "/api")
    {
        var opt = new OtterApiOptions { Path = path };
        return opt;
    }

    // ── Route construction ────────────────────────────────────────────────────

    [Fact]
    public void Build_CombinesOptionsPathWithEntityRoute()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        Assert.Equal("/api/products", entity.Route);
    }

    [Fact]
    public void Build_AddsLeadingSlash_WhenRouteDoesNotStartWithSlash()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("products").Build(typeof(TestDbContext), options);

        Assert.Equal("/api/products", entity.Route);
    }

    [Fact]
    public void Build_WorksWithEmptyBasePath()
    {
        var options = OptionsWithPath(string.Empty);
        var entity  = options.Entity<TestProduct>("/items").Build(typeof(TestDbContext), options);

        Assert.Equal("/items", entity.Route);
    }

    // ── Id detection via [Key] attribute ──────────────────────────────────────

    [Fact]
    public void Build_DetectsId_FromKeyAttribute()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        Assert.NotNull(entity.Id);
        Assert.Equal("Id", entity.Id.Name);
    }

    [Fact]
    public void Build_IdIsNull_ForKeylessEntity()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<KeylessEntity>("/kl").Build(typeof(KeylessDbContext), options);

        Assert.Null(entity.Id);
    }

    // ── Scalar properties vs navigation properties ────────────────────────────

    [Fact]
    public void Build_Properties_ContainsOnlyScalarTypes()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        // TestProduct has Id(int), Name(string), Price(decimal), CategoryId(int) as scalars
        var names = entity.Properties.Select(p => p.Name).ToList();

        Assert.Contains("Id",         names);
        Assert.Contains("Name",       names);
        Assert.Contains("Price",      names);
        Assert.Contains("CategoryId", names);

        // Category is a navigation property and must NOT appear here
        Assert.DoesNotContain("Category", names);
    }

    [Fact]
    public void Build_NavigationProperties_ContainsNonScalarTypes()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        var names = entity.NavigationProperties.Select(p => p.Name).ToList();

        Assert.Contains("Category", names);
        Assert.DoesNotContain("Name", names);
    }

    // ── DbSet reference ───────────────────────────────────────────────────────

    [Fact]
    public void Build_SetsDbSetProperty_PointingToCorrectDbSetOnContext()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        Assert.NotNull(entity.DbSet);
        Assert.Equal("Products", entity.DbSet.Name);
    }

    [Fact]
    public void Build_Throws_WhenNoDbSetFoundForEntity()
    {
        // TestProduct is not in KeylessDbContext
        var options = OptionsWithPath("/api");
        Assert.Throws<InvalidOperationException>(
            () => options.Entity<TestProduct>("/products").Build(typeof(KeylessDbContext), options));
    }

    // ── Policy assignments ────────────────────────────────────────────────────

    [Fact]
    public void Build_AssignsPolicies_AsConfigured()
    {
        var options = OptionsWithPath("/api");
        var builder = options.Entity<TestProduct>("/products")
            .WithEntityPolicy("AdminOnly")
            .WithGetPolicy("ReadPolicy")
            .WithPostPolicy("WritePolicy")
            .WithPutPolicy("WritePolicy")
            .WithDeletePolicy("AdminOnly");

        var entity = builder.Build(typeof(TestDbContext), options);

        Assert.Equal("AdminOnly",   entity.EntityPolicy);
        Assert.Equal("ReadPolicy",  entity.GetPolicy);
        Assert.Equal("WritePolicy", entity.PostPolicy);
        Assert.Equal("WritePolicy", entity.PutPolicy);
        Assert.Equal("AdminOnly",   entity.DeletePolicy);
    }

    // ── Authorization flag ────────────────────────────────────────────────────

    [Fact]
    public void Build_Authorize_IsFalseByDefault()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        Assert.False(entity.Authorize);
    }

    [Fact]
    public void Build_Authorize_IsTrue_WhenSetViaFluent()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Authorize().Build(typeof(TestDbContext), options);

        Assert.True(entity.Authorize);
    }

    // ── AllowedOperations ─────────────────────────────────────────────────────

    [Fact]
    public void Build_AllowedOperations_IsAll_ByDefault()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        Assert.Equal(OtterApiCrudOperation.All, entity.AllowedOperations);
    }

    [Fact]
    public void Build_AllowedOperations_ReflectsFluentAllow()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products")
            .Allow(OtterApiCrudOperation.Get | OtterApiCrudOperation.Post)
            .Build(typeof(TestDbContext), options);

        Assert.Equal(OtterApiCrudOperation.Get | OtterApiCrudOperation.Post, entity.AllowedOperations);
    }

    // ── ExposePagedResult ─────────────────────────────────────────────────────

    [Fact]
    public void Build_ExposePagedResult_IsFalseByDefault()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        Assert.False(entity.ExposePagedResult);
    }

    [Fact]
    public void Build_ExposePagedResult_IsTrueWhenSet()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").ExposePagedResult().Build(typeof(TestDbContext), options);

        Assert.True(entity.ExposePagedResult);
    }

    // ── Save handlers ─────────────────────────────────────────────────────────

    [Fact]
    public void Build_PreSaveHandler_IsNull_ByDefault()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        Assert.Empty(entity.PreSaveHandlers);
    }

    [Fact]
    public void Build_PostSaveHandler_IsNull_ByDefault()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        Assert.Empty(entity.PostSaveHandlers);
    }

    [Fact]
    public async Task Build_PreSaveHandler_IsCallable_WhenSet()
    {
        bool called = false;
        var options = OptionsWithPath("/api");
        var apiEntity = options.Entity<TestProduct>("/products")
            .BeforeSave((ctx, entity, original, op) => { called = true; })
            .Build(typeof(TestDbContext), options);

        using var db  = DbContextFactory.CreateInMemory();
        var product   = new TestProduct { Id = 1, Name = "X" };

        Assert.Single(apiEntity.PreSaveHandlers);
        await apiEntity.PreSaveHandlers[0](db, product, null, OtterApiCrudOperation.Post);

        Assert.True(called);
    }

    [Fact]
    public async Task Build_PostSaveHandler_IsCallable_WhenSet()
    {
        bool called = false;
        var options = OptionsWithPath("/api");
        var apiEntity = options.Entity<TestProduct>("/products")
            .AfterSave((ctx, entity, original, op) => { called = true; })
            .Build(typeof(TestDbContext), options);

        using var db  = DbContextFactory.CreateInMemory();
        var product   = new TestProduct { Id = 1, Name = "X" };

        Assert.Single(apiEntity.PostSaveHandlers);
        await apiEntity.PostSaveHandlers[0](db, product, null, OtterApiCrudOperation.Post);

        Assert.True(called);
    }

    // ── DbContextType ─────────────────────────────────────────────────────────

    [Fact]
    public void Build_SetsDbContextType_FromGenericArgument()
    {
        var options = OptionsWithPath("/api");
        var entity  = options.Entity<TestProduct>("/products").Build(typeof(TestDbContext), options);

        Assert.Equal(typeof(TestDbContext), entity.DbContextType);
    }
}








