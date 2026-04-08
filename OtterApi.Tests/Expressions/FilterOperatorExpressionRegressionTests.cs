using System.Reflection;
using OtterApi.Expressions;
using Xunit;

namespace OtterApi.Tests.Expressions;

/// <summary>
/// Regression tests for confirmed production bugs in OtterApiFilterOperatorExpression.
/// </summary>
public class FilterOperatorExpressionRegressionTests
{
    private static PropertyInfo Prop<T>(string name) =>
        typeof(T).GetProperty(name)!;

    private class Stub
    {
        public int    Age  { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // ── Bug A: "in"/"nin" detection was case-sensitive ────────────────────────
    // IsOperatorSuported accepts "IN"/"NIN" (case-insensitive), but the internal
    // Contains check was case-sensitive, causing "[1,2,3]" to be treated as a
    // scalar value → FormatException instead of JSON deserialization.

    [Theory]
    [InlineData("IN")]
    [InlineData("In")]
    [InlineData("iN")]
    public void Build_In_CaseInsensitive_DeserializesJsonArray(string op)
    {
        var prop   = Prop<Stub>("Age");
        var result = new OtterApiFilterOperatorExpression(prop, "[1,2,3]", 0, op).Build();

        Assert.Contains("Contains", result.Filter);
        var list = result.Values[0] as List<int>;
        Assert.NotNull(list);
        Assert.Equal([1, 2, 3], list);
    }

    [Theory]
    [InlineData("NIN")]
    [InlineData("Nin")]
    [InlineData("nIn")]
    public void Build_Nin_CaseInsensitive_DeserializesJsonArray(string op)
    {
        var prop   = Prop<Stub>("Name");
        var result = new OtterApiFilterOperatorExpression(prop, "[\"a\",\"b\"]", 0, op).Build();

        Assert.Contains("Contains", result.Filter);
        var list = result.Values[0] as List<string>;
        Assert.NotNull(list);
        Assert.Equal(["a", "b"], list);
    }

    // ── The uppercase-operator filter expression must still use @index ─────────

    [Fact]
    public void Build_In_Uppercase_FilterContainsCorrectIndex()
    {
        var prop   = Prop<Stub>("Age");
        var result = new OtterApiFilterOperatorExpression(prop, "[10]", 2, "IN").Build();

        Assert.Contains("@2", result.Filter);
        Assert.Equal(3, result.NextIndex);
    }
}

