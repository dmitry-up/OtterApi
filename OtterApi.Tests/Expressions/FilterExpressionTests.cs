using System.Reflection;
using OtterApi.Expressions;
using Xunit;

namespace OtterApi.Tests.Expressions;

/// <summary>
/// Contract: OtterApiFilterOtterApiExpression builds an equality filter fragment
/// ("Property == @N") with the correctly typed value and advances the index.
/// </summary>
public class FilterExpressionTests
{
    private static PropertyInfo Prop<T>(string name) =>
        typeof(T).GetProperty(name)!;

    // ── Basic equality fragments ───────────────────────────────────────────────

    [Fact]
    public void Build_GeneratesEqualityFragment_ForStringProperty()
    {
        var prop   = Prop<FilterStub>("Name");
        var result = new OtterApiFilterOtterApiExpression(prop, "Alice", 0).Build();

        Assert.Equal("Name == @0", result.Filter);
    }

    [Fact]
    public void Build_GeneratesEqualityFragment_ForIntProperty()
    {
        var prop   = Prop<FilterStub>("Age");
        var result = new OtterApiFilterOtterApiExpression(prop, "42", 0).Build();

        Assert.Equal("Age == @0", result.Filter);
    }

    // ── Value is converted to the property's type ─────────────────────────────

    [Fact]
    public void Build_ConvertsStringValue_ToInt()
    {
        var prop   = Prop<FilterStub>("Age");
        var result = new OtterApiFilterOtterApiExpression(prop, "99", 0).Build();

        Assert.Single(result.Values);
        Assert.IsType<int>(result.Values[0]);
        Assert.Equal(99, result.Values[0]);
    }

    [Fact]
    public void Build_KeepsStringValue_AsString()
    {
        var prop   = Prop<FilterStub>("Name");
        var result = new OtterApiFilterOtterApiExpression(prop, "Bob", 0).Build();

        Assert.Single(result.Values);
        Assert.IsType<string>(result.Values[0]);
        Assert.Equal("Bob", result.Values[0]);
    }

    [Fact]
    public void Build_ConvertsStringValue_ToDecimal()
    {
        var prop   = Prop<FilterStub>("Price");
        var result = new OtterApiFilterOtterApiExpression(prop, "9.99", 0).Build();

        Assert.IsType<decimal>(result.Values[0]);
        Assert.Equal(9.99m, result.Values[0]);
    }

    [Fact]
    public void Build_ConvertsStringValue_ToGuid()
    {
        var id   = Guid.NewGuid();
        var prop = Prop<FilterStub>("Token");
        var result = new OtterApiFilterOtterApiExpression(prop, id.ToString(), 0).Build();

        Assert.IsType<Guid>(result.Values[0]);
        Assert.Equal(id, result.Values[0]);
    }

    // ── Index tracking ────────────────────────────────────────────────────────

    [Fact]
    public void Build_SetsNextIndex_ToIndexPlusOne()
    {
        var prop   = Prop<FilterStub>("Age");
        var result = new OtterApiFilterOtterApiExpression(prop, "1", 3).Build();

        Assert.Equal(4, result.NextIndex);
    }

    [Fact]
    public void Build_UsesSuppliedIndex_InFilterFragment()
    {
        var prop   = Prop<FilterStub>("Name");
        var result = new OtterApiFilterOtterApiExpression(prop, "x", 5).Build();

        Assert.Equal("Name == @5", result.Filter);
    }

    // ── Consecutive expressions share a growing index ─────────────────────────

    [Fact]
    public void Build_ChainedFilters_UseMonotonicallyIncreasingIndexes()
    {
        var nameProp  = Prop<FilterStub>("Name");
        var ageProp   = Prop<FilterStub>("Age");

        var first  = new OtterApiFilterOtterApiExpression(nameProp, "Alice", 0).Build();
        var second = new OtterApiFilterOtterApiExpression(ageProp,  "30",   first.NextIndex).Build();

        Assert.Equal("Name == @0", first.Filter);
        Assert.Equal("Age == @1",  second.Filter);
        Assert.Equal(2, second.NextIndex);
    }

    // ── Stub type ─────────────────────────────────────────────────────────────

    private class FilterStub
    {
        public string  Name  { get; set; } = string.Empty;
        public int     Age   { get; set; }
        public decimal Price { get; set; }
        public Guid    Token { get; set; }
    }
}

