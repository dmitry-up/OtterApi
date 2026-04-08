using System.Linq.Expressions;
using System.Reflection;
using OtterApi.Expressions;
using Xunit;

namespace OtterApi.Tests.Expressions;

/// <summary>
/// Contract: OtterApiFilterOtterApiExpression builds a typed equality predicate
/// (Expression&lt;Func&lt;T, bool&gt;&gt;) that correctly filters in-memory and database queries.
/// </summary>
public class FilterExpressionTests
{
    private static PropertyInfo Prop<T>(string name) =>
        typeof(T).GetProperty(name)!;

    /// <summary>Compile and apply the predicate lambda to a sequence of T.</summary>
    private static List<T> Apply<T>(LambdaExpression predicate, IEnumerable<T> data)
    {
        var typedLambda = (Expression<Func<T, bool>>)predicate;
        return data.AsQueryable().Where(typedLambda).ToList();
    }

    // ── Basic equality predicates ─────────────────────────────────────────────

    [Fact]
    public void Build_EqualityPredicate_ForStringProperty_MatchesCorrectItem()
    {
        var prop   = Prop<FilterStub>("Name");
        var result = new OtterApiFilterOtterApiExpression(prop, "Alice").Build();

        Assert.NotNull(result.Predicate);

        var stubs   = new[] { new FilterStub { Name = "Alice" }, new FilterStub { Name = "Bob" } };
        var matched = Apply<FilterStub>(result.Predicate!, stubs);

        Assert.Single(matched);
        Assert.Equal("Alice", matched[0].Name);
    }

    [Fact]
    public void Build_EqualityPredicate_ForIntProperty_MatchesCorrectItem()
    {
        var prop   = Prop<FilterStub>("Age");
        var result = new OtterApiFilterOtterApiExpression(prop, "42").Build();

        Assert.NotNull(result.Predicate);

        var stubs   = new[] { new FilterStub { Age = 42 }, new FilterStub { Age = 7 } };
        var matched = Apply<FilterStub>(result.Predicate!, stubs);

        Assert.Single(matched);
        Assert.Equal(42, matched[0].Age);
    }

    // ── Value is converted to the property's type ─────────────────────────────

    [Fact]
    public void Build_ConvertsStringValue_ToInt_PredicateFiltersCorrectly()
    {
        var prop    = Prop<FilterStub>("Age");
        var result  = new OtterApiFilterOtterApiExpression(prop, "99").Build();
        var stubs   = new[] { new FilterStub { Age = 99 }, new FilterStub { Age = 0 } };
        var matched = Apply<FilterStub>(result.Predicate!, stubs);

        Assert.Single(matched);
        Assert.Equal(99, matched[0].Age);
    }

    [Fact]
    public void Build_ConvertsStringValue_ToDecimal_PredicateFiltersCorrectly()
    {
        var prop    = Prop<FilterStub>("Price");
        var result  = new OtterApiFilterOtterApiExpression(prop, "9.99").Build();
        var stubs   = new[] { new FilterStub { Price = 9.99m }, new FilterStub { Price = 1.00m } };
        var matched = Apply<FilterStub>(result.Predicate!, stubs);

        Assert.Single(matched);
        Assert.Equal(9.99m, matched[0].Price);
    }

    [Fact]
    public void Build_ConvertsStringValue_ToGuid_PredicateFiltersCorrectly()
    {
        var id      = Guid.NewGuid();
        var prop    = Prop<FilterStub>("Token");
        var result  = new OtterApiFilterOtterApiExpression(prop, id.ToString()).Build();
        var stubs   = new[] { new FilterStub { Token = id }, new FilterStub { Token = Guid.NewGuid() } };
        var matched = Apply<FilterStub>(result.Predicate!, stubs);

        Assert.Single(matched);
        Assert.Equal(id, matched[0].Token);
    }

    // ── No match returns empty ────────────────────────────────────────────────

    [Fact]
    public void Build_PredicateReturnsEmpty_WhenNoMatch()
    {
        var prop    = Prop<FilterStub>("Name");
        var result  = new OtterApiFilterOtterApiExpression(prop, "NonExistent").Build();
        var stubs   = new[] { new FilterStub { Name = "Alice" }, new FilterStub { Name = "Bob" } };
        var matched = Apply<FilterStub>(result.Predicate!, stubs);

        Assert.Empty(matched);
    }

    // ── Multiple independent predicates each filter correctly ─────────────────

    [Fact]
    public void Build_TwoIndependentPredicates_EachFiltersCorrectly()
    {
        var nameProp = Prop<FilterStub>("Name");
        var ageProp  = Prop<FilterStub>("Age");

        var namePred = new OtterApiFilterOtterApiExpression(nameProp, "Alice").Build();
        var agePred  = new OtterApiFilterOtterApiExpression(ageProp,  "30").Build();

        var stubs = new[]
        {
            new FilterStub { Name = "Alice", Age = 30 },
            new FilterStub { Name = "Alice", Age = 25 },
            new FilterStub { Name = "Bob",   Age = 30 },
        };

        var nameMatches = Apply<FilterStub>(namePred.Predicate!, stubs);
        var ageMatches  = Apply<FilterStub>(agePred.Predicate!,  stubs);

        Assert.Equal(2, nameMatches.Count);   // Alice-30, Alice-25
        Assert.Equal(2, ageMatches.Count);    // Alice-30, Bob-30
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

