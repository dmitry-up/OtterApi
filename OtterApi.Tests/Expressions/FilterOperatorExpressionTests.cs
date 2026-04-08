using System.Reflection;
using System.Text.Json;
using OtterApi.Expressions;
using Xunit;

namespace OtterApi.Tests.Expressions;

/// <summary>
/// Contract: OtterApiFilterOperatorExpression translates a named comparison operator
/// into the correct Dynamic LINQ fragment and throws for unsupported type/operator combos.
/// </summary>
public class FilterOperatorExpressionTests
{
    private static PropertyInfo Prop<T>(string name) =>
        typeof(T).GetProperty(name)!;

    // ── eq ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_Eq_OnString_GeneratesEquality()
    {
        var prop   = Prop<Stub>("Name");
        var result = new OtterApiFilterOperatorExpression(prop, "Alice", 0, "eq").Build();

        Assert.Equal("Name == @0", result.Filter);
        Assert.Equal("Alice", result.Values[0]);
    }

    [Fact]
    public void Build_Eq_OnInt_GeneratesEquality()
    {
        var prop   = Prop<Stub>("Age");
        var result = new OtterApiFilterOperatorExpression(prop, "25", 0, "eq").Build();

        Assert.Equal("Age == @0", result.Filter);
        Assert.Equal(25, result.Values[0]);
    }

    [Fact]
    public void Build_Eq_OnGuid_GeneratesEquality()
    {
        var id   = Guid.NewGuid();
        var prop = Prop<Stub>("Token");
        var result = new OtterApiFilterOperatorExpression(prop, id.ToString(), 0, "eq").Build();

        Assert.Equal("Token == @0", result.Filter);
        Assert.Equal(id, result.Values[0]);
    }

    // ── neq ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_Neq_OnInt_GeneratesNotEquality()
    {
        var prop   = Prop<Stub>("Age");
        var result = new OtterApiFilterOperatorExpression(prop, "10", 0, "neq").Build();

        Assert.Equal("Age != @0", result.Filter);
    }

    // ── like / nlike ─────────────────────────────────────────────────────────

    [Fact]
    public void Build_Like_OnString_GeneratesContains()
    {
        var prop   = Prop<Stub>("Name");
        var result = new OtterApiFilterOperatorExpression(prop, "ali", 0, "like").Build();

        Assert.Equal("Name.Contains(@0)", result.Filter);
        Assert.Equal("ali", result.Values[0]);
    }

    [Fact]
    public void Build_Nlike_OnString_GeneratesNotContains()
    {
        var prop   = Prop<Stub>("Name");
        var result = new OtterApiFilterOperatorExpression(prop, "ali", 0, "nlike").Build();

        Assert.Equal("!Name.Contains(@0)", result.Filter);
    }

    // ── Numeric comparison operators ──────────────────────────────────────────

    [Theory]
    [InlineData("lt",   "Age < @0")]
    [InlineData("lteq", "Age <= @0")]
    [InlineData("gt",   "Age > @0")]
    [InlineData("gteq", "Age >= @0")]
    public void Build_NumericOperator_OnInt_GeneratesCorrectFragment(string op, string expected)
    {
        var prop   = Prop<Stub>("Age");
        var result = new OtterApiFilterOperatorExpression(prop, "18", 0, op).Build();

        Assert.Equal(expected, result.Filter);
        Assert.Equal(18, result.Values[0]);
    }

    // ── in / nin ─────────────────────────────────────────────────────────────

    [Fact]
    public void Build_In_OnInt_DeserializesJsonArray()
    {
        var prop   = Prop<Stub>("Age");
        var result = new OtterApiFilterOperatorExpression(prop, "[1,2,3]", 0, "in").Build();

        Assert.Equal("@0.Contains(Age)", result.Filter);
        Assert.Single(result.Values);

        var list = result.Values[0] as List<int>;
        Assert.NotNull(list);
        Assert.Equal([1, 2, 3], list);
    }

    [Fact]
    public void Build_Nin_OnString_DeserializesJsonArray()
    {
        var prop   = Prop<Stub>("Name");
        var result = new OtterApiFilterOperatorExpression(prop, "[\"a\",\"b\"]", 0, "nin").Build();

        Assert.Equal("!@0.Contains(Name)", result.Filter);
        var list = result.Values[0] as List<string>;
        Assert.NotNull(list);
        Assert.Equal(["a", "b"], list);
    }

    // ── Operator is case-insensitive ──────────────────────────────────────────

    [Theory]
    [InlineData("EQ")]
    [InlineData("Eq")]
    public void Build_Operator_IsCaseInsensitive(string op)
    {
        var prop   = Prop<Stub>("Age");
        var result = new OtterApiFilterOperatorExpression(prop, "5", 0, op).Build();

        Assert.Equal("Age == @0", result.Filter);
    }

    // ── Index is embedded in fragment and NextIndex is incremented ────────────

    [Fact]
    public void Build_NextIndex_IsIncrementedByOne()
    {
        var prop   = Prop<Stub>("Age");
        var result = new OtterApiFilterOperatorExpression(prop, "7", 2, "eq").Build();

        Assert.Contains("@2", result.Filter);
        Assert.Equal(3, result.NextIndex);
    }

    // ── Unsupported type+operator combos throw ────────────────────────────────

    [Fact]
    public void Build_Like_OnInt_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Age");
        Assert.Throws<NotSupportedException>(
            () => new OtterApiFilterOperatorExpression(prop, "5", 0, "like").Build());
    }

    [Fact]
    public void Build_Lt_OnString_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Name");
        Assert.Throws<NotSupportedException>(
            () => new OtterApiFilterOperatorExpression(prop, "x", 0, "lt").Build());
    }

    [Fact]
    public void Build_Lt_OnGuid_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Token");
        Assert.Throws<NotSupportedException>(
            () => new OtterApiFilterOperatorExpression(prop, Guid.NewGuid().ToString(), 0, "lt").Build());
    }

    [Fact]
    public void Build_Like_OnGuid_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Token");
        Assert.Throws<NotSupportedException>(
            () => new OtterApiFilterOperatorExpression(prop, "abc", 0, "like").Build());
    }

    // ── Unknown operator throws ───────────────────────────────────────────────

    [Fact]
    public void Build_UnknownOperator_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Age");
        Assert.Throws<NotSupportedException>(
            () => new OtterApiFilterOperatorExpression(prop, "5", 0, "between").Build());
    }

    // ── Nullable value types are unwrapped before operator check ─────────────

    [Fact]
    public void Build_Eq_OnNullableInt_Succeeds()
    {
        var prop   = Prop<Stub>("NullableAge");
        var result = new OtterApiFilterOperatorExpression(prop, "10", 0, "eq").Build();

        Assert.Equal("NullableAge == @0", result.Filter);
    }

    [Fact]
    public void Build_Gt_OnNullableInt_Succeeds()
    {
        var prop   = Prop<Stub>("NullableAge");
        var result = new OtterApiFilterOperatorExpression(prop, "5", 0, "gt").Build();

        Assert.Equal("NullableAge > @0", result.Filter);
    }

    // ── Stub ─────────────────────────────────────────────────────────────────

    private class Stub
    {
        public string  Name        { get; set; } = string.Empty;
        public int     Age         { get; set; }
        public Guid    Token       { get; set; }
        public int?    NullableAge { get; set; }
    }
}

