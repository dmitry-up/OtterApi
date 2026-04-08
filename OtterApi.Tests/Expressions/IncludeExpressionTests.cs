using System.Reflection;
using OtterApi.Expressions;
using OtterApi.Models;
using Xunit;

namespace OtterApi.Tests.Expressions;

/// <summary>
/// Contract: OtterApiIncludeOtterApiExpression resolves comma-separated
/// include names against the entity's NavigationProperties, ignoring unknown
/// names and preserving the actual property-name casing.
/// </summary>
public class IncludeExpressionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OtterApiEntity BuildEntity(params string[] navPropNames)
    {
        var navProps = navPropNames
            .Select(n => typeof(NavStub).GetProperty(n)
                         ?? throw new InvalidOperationException($"NavStub has no property '{n}'"))
            .ToList();

        return new OtterApiEntity
        {
            NavigationProperties = navProps,
            Properties = [],
        };
    }

    // Helper type whose property names we reference in tests
    private class NavStub
    {
        public object? Orders   { get; set; }
        public object? Customer { get; set; }
        public object? Tags     { get; set; }
    }

    // ── Single valid include ───────────────────────────────────────────────────

    [Fact]
    public void Build_ReturnsSingleNavProp_WhenRequestedByExactName()
    {
        var entity = BuildEntity("Orders", "Customer");
        var result = new OtterApiIncludeOtterApiExpression(entity, "Orders").Build();

        Assert.Equal(["Orders"], result);
    }

    // ── Case-insensitive matching ─────────────────────────────────────────────

    [Fact]
    public void Build_MatchesNavProp_CaseInsensitively()
    {
        var entity = BuildEntity("Orders", "Customer");
        var result = new OtterApiIncludeOtterApiExpression(entity, "orders").Build();

        // Returned name must preserve the actual property casing ("Orders")
        Assert.Equal(["Orders"], result);
    }

    [Fact]
    public void Build_MatchesNavProp_UpperCase()
    {
        var entity = BuildEntity("Customer");
        var result = new OtterApiIncludeOtterApiExpression(entity, "CUSTOMER").Build();

        Assert.Equal(["Customer"], result);
    }

    // ── Multiple valid includes ───────────────────────────────────────────────

    [Fact]
    public void Build_ReturnsMultipleNavProps_ForCommaSeparatedList()
    {
        var entity = BuildEntity("Orders", "Customer", "Tags");
        var result = new OtterApiIncludeOtterApiExpression(entity, "Orders,Customer").Build();

        Assert.Contains("Orders",   result);
        Assert.Contains("Customer", result);
        Assert.Equal(2, result.Count);
    }

    // ── Unknown names are silently ignored ────────────────────────────────────

    [Fact]
    public void Build_IgnoresUnknownNavPropNames()
    {
        var entity = BuildEntity("Orders");
        var result = new OtterApiIncludeOtterApiExpression(entity, "Orders,NonExistent").Build();

        Assert.Equal(["Orders"], result);
    }

    [Fact]
    public void Build_ReturnsEmpty_WhenNoNamesMatch()
    {
        var entity = BuildEntity("Orders");
        var result = new OtterApiIncludeOtterApiExpression(entity, "Foo,Bar").Build();

        Assert.Empty(result);
    }

    // ── Empty / whitespace values ─────────────────────────────────────────────

    [Fact]
    public void Build_ReturnsEmpty_ForEmptyString()
    {
        var entity = BuildEntity("Orders");
        var result = new OtterApiIncludeOtterApiExpression(entity, "").Build();

        Assert.Empty(result);
    }

    [Fact]
    public void Build_IgnoresEmptySegments_InCommaSeparatedList()
    {
        var entity = BuildEntity("Orders", "Customer");
        var result = new OtterApiIncludeOtterApiExpression(entity, "Orders,,Customer").Build();

        Assert.Contains("Orders",   result);
        Assert.Contains("Customer", result);
        Assert.Equal(2, result.Count);
    }

    // ── Entity with no nav properties ─────────────────────────────────────────

    [Fact]
    public void Build_ReturnsEmpty_WhenEntityHasNoNavProperties()
    {
        var entity = new OtterApiEntity { NavigationProperties = [], Properties = [] };
        var result = new OtterApiIncludeOtterApiExpression(entity, "Orders").Build();

        Assert.Empty(result);
    }

    // ── No duplicate entries ─────────────────────────────────────────────────

    [Fact]
    public void Build_DoesNotReturnDuplicates_WhenSameNavPropRequestedTwice()
    {
        var entity = BuildEntity("Orders");
        // Intersect deduplicates the first list, so "Orders,orders" → ["Orders"]
        var result = new OtterApiIncludeOtterApiExpression(entity, "Orders,orders").Build();

        Assert.Single(result);
        Assert.Equal("Orders", result[0]);
    }
}

