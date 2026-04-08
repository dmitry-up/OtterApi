using System.Linq.Expressions;
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

    private static List<T> Apply<T>(LambdaExpression predicate, IEnumerable<T> data)
    {
        var typedLambda = (Expression<Func<T, bool>>)predicate;
        return data.AsQueryable().Where(typedLambda).ToList();
    }

    // ── Bug A: "in"/"nin" detection was case-sensitive ────────────────────────
    // IsOperatorSuported accepts "IN"/"NIN" (case-insensitive), but the internal
    // Contains check was case-sensitive, causing "[1,2,3]" to be treated as a
    // scalar value → FormatException instead of JSON deserialization.

    [Theory]
    [InlineData("IN")]
    [InlineData("In")]
    [InlineData("iN")]
    public void Build_In_CaseInsensitive_FiltersCorrectly(string op)
    {
        var prop    = Prop<Stub>("Age");
        var result  = new OtterApiFilterOperatorExpression(prop, "[1,2,3]", op).Build();

        Assert.NotNull(result.Predicate);

        var data    = new[] { new Stub { Age = 1 }, new Stub { Age = 4 }, new Stub { Age = 2 } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Equal(2, matched.Count);
        Assert.All(matched, s => Assert.Contains(s.Age, new[] { 1, 2, 3 }));
    }

    [Theory]
    [InlineData("NIN")]
    [InlineData("Nin")]
    [InlineData("nIn")]
    public void Build_Nin_CaseInsensitive_FiltersCorrectly(string op)
    {
        var prop    = Prop<Stub>("Name");
        var result  = new OtterApiFilterOperatorExpression(prop, "[\"a\",\"b\"]", op).Build();

        Assert.NotNull(result.Predicate);

        var data    = new[] { new Stub { Name = "a" }, new Stub { Name = "c" }, new Stub { Name = "b" } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal("c", matched[0].Name);
    }

    // ── Uppercase "IN" operator builds a valid predicate ─────────────────────

    [Fact]
    public void Build_In_Uppercase_BuildsValidPredicate()
    {
        var prop    = Prop<Stub>("Age");
        var result  = new OtterApiFilterOperatorExpression(prop, "[10]", "IN").Build();

        Assert.NotNull(result.Predicate);

        var data    = new[] { new Stub { Age = 10 }, new Stub { Age = 5 } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal(10, matched[0].Age);
    }
}
