using System.Linq.Expressions;
using System.Reflection;
using OtterApi.Exceptions;
using OtterApi.Expressions;
using Xunit;

namespace OtterApi.Tests.Expressions;

/// <summary>
/// Contract: OtterApiFilterOperatorExpression builds a typed predicate for the given
/// comparison operator that correctly filters in-memory and database queries.
/// </summary>
public class FilterOperatorExpressionTests
{
    private static PropertyInfo Prop<T>(string name) =>
        typeof(T).GetProperty(name)!;

    private static List<T> Apply<T>(LambdaExpression predicate, IEnumerable<T> data)
    {
        var typedLambda = (Expression<Func<T, bool>>)predicate;
        return data.AsQueryable().Where(typedLambda).ToList();
    }

    // ── eq ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_Eq_OnString_MatchesExactValue()
    {
        var prop    = Prop<Stub>("Name");
        var result  = new OtterApiFilterOperatorExpression(prop, "Alice", "eq").Build();
        var data    = new[] { new Stub { Name = "Alice" }, new Stub { Name = "Bob" } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal("Alice", matched[0].Name);
    }

    [Fact]
    public void Build_Eq_OnInt_MatchesExactValue()
    {
        var prop    = Prop<Stub>("Age");
        var result  = new OtterApiFilterOperatorExpression(prop, "25", "eq").Build();
        var data    = new[] { new Stub { Age = 25 }, new Stub { Age = 30 } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal(25, matched[0].Age);
    }

    [Fact]
    public void Build_Eq_OnGuid_MatchesExactValue()
    {
        var id      = Guid.NewGuid();
        var prop    = Prop<Stub>("Token");
        var result  = new OtterApiFilterOperatorExpression(prop, id.ToString(), "eq").Build();
        var data    = new[] { new Stub { Token = id }, new Stub { Token = Guid.NewGuid() } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal(id, matched[0].Token);
    }

    // ── neq ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_Neq_OnInt_ExcludesMatchingValue()
    {
        var prop    = Prop<Stub>("Age");
        var result  = new OtterApiFilterOperatorExpression(prop, "10", "neq").Build();
        var data    = new[] { new Stub { Age = 10 }, new Stub { Age = 20 } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal(20, matched[0].Age);
    }

    // ── like / nlike ─────────────────────────────────────────────────────────

    [Fact]
    public void Build_Like_OnString_MatchesSubstring()
    {
        var prop    = Prop<Stub>("Name");
        var result  = new OtterApiFilterOperatorExpression(prop, "ali", "like").Build();
        var data    = new[] { new Stub { Name = "alice" }, new Stub { Name = "Bob" }, new Stub { Name = "alias" } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Equal(2, matched.Count);
        Assert.All(matched, s => Assert.Contains("ali", s.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_Like_OnString_IsCaseInsensitive()
    {
        var prop    = Prop<Stub>("Name");
        var result  = new OtterApiFilterOperatorExpression(prop, "ALI", "like").Build();
        var data    = new[] { new Stub { Name = "alice" }, new Stub { Name = "Bob" }, new Stub { Name = "ALIAS" } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Equal(2, matched.Count);
    }

    [Fact]
    public void Build_Nlike_OnString_ExcludesSubstringMatches()
    {
        var prop    = Prop<Stub>("Name");
        var result  = new OtterApiFilterOperatorExpression(prop, "ali", "nlike").Build();
        var data    = new[] { new Stub { Name = "alice" }, new Stub { Name = "Bob" } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal("Bob", matched[0].Name);
    }

    [Fact]
    public void Build_Nlike_OnString_IsCaseInsensitive()
    {
        var prop    = Prop<Stub>("Name");
        var result  = new OtterApiFilterOperatorExpression(prop, "ALI", "nlike").Build();
        var data    = new[] { new Stub { Name = "alice" }, new Stub { Name = "Bob" }, new Stub { Name = "ALIAS" } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal("Bob", matched[0].Name);
    }

    // ── Numeric comparison operators ──────────────────────────────────────────

    [Theory]
    [InlineData("lt",   new[] { 10, 11, 17 }, 3)]   // < 18: all three
    [InlineData("lteq", new[] { 18, 17, 20 }, 2)]   // <= 18: 18, 17
    [InlineData("gt",   new[] { 19, 18, 17 }, 1)]   // > 18: only 19
    [InlineData("gteq", new[] { 18, 19, 17 }, 2)]   // >= 18: 18, 19
    public void Build_NumericOperator_OnInt_FiltersCorrectly(string op, int[] ages, int expectedCount)
    {
        var prop    = Prop<Stub>("Age");
        var result  = new OtterApiFilterOperatorExpression(prop, "18", op).Build();
        var data    = ages.Select(a => new Stub { Age = a });
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Equal(expectedCount, matched.Count);
    }

    // ── in / nin ─────────────────────────────────────────────────────────────

    [Fact]
    public void Build_In_OnInt_MatchesItemsInSet()
    {
        var prop    = Prop<Stub>("Age");
        var result  = new OtterApiFilterOperatorExpression(prop, "[1,2,3]", "in").Build();
        var data    = new[] { new Stub { Age = 1 }, new Stub { Age = 4 }, new Stub { Age = 2 } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Equal(2, matched.Count);
        Assert.All(matched, s => Assert.Contains(s.Age, new[] { 1, 2, 3 }));
    }

    [Fact]
    public void Build_Nin_OnString_ExcludesItemsInSet()
    {
        var prop    = Prop<Stub>("Name");
        var result  = new OtterApiFilterOperatorExpression(prop, "[\"a\",\"b\"]", "nin").Build();
        var data    = new[] { new Stub { Name = "a" }, new Stub { Name = "c" }, new Stub { Name = "b" } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal("c", matched[0].Name);
    }

    // ── in / nin with enum properties ────────────────────────────────────────

    private static System.Text.Json.JsonSerializerOptions EnumOptions()
    {
        var opts = new System.Text.Json.JsonSerializerOptions();
        opts.Converters.Add(new OtterApi.Converters.OtterApiCaseInsensitiveEnumConverterFactory());
        return opts;
    }

    [Fact]
    public void Build_In_OnEnum_AcceptsStringNames()
    {
        // filter[status][in]=["Active","Pending"] — string enum names must work
        var prop    = Prop<Stub>("Status");
        var result  = new OtterApiFilterOperatorExpression(prop, "[\"Active\",\"Pending\"]", "in", EnumOptions()).Build();
        var data    = new[]
        {
            new Stub { Status = StubStatus.Active },
            new Stub { Status = StubStatus.Deleted },
            new Stub { Status = StubStatus.Pending }
        };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Equal(2, matched.Count);
        Assert.All(matched, s => Assert.NotEqual(StubStatus.Deleted, s.Status));
    }

    [Fact]
    public void Build_In_OnEnum_AcceptsStringNames_CaseInsensitive()
    {
        // filter[status][in]=["active","PENDING"] — case-insensitive
        var prop    = Prop<Stub>("Status");
        var result  = new OtterApiFilterOperatorExpression(prop, "[\"active\",\"PENDING\"]", "in", EnumOptions()).Build();
        var data    = new[]
        {
            new Stub { Status = StubStatus.Active },
            new Stub { Status = StubStatus.Deleted },
            new Stub { Status = StubStatus.Pending }
        };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Equal(2, matched.Count);
    }

    [Fact]
    public void Build_In_OnEnum_AcceptsIntegerValues()
    {
        // filter[status][in]=[0,1] — integer enum values must still work
        var prop    = Prop<Stub>("Status");
        var result  = new OtterApiFilterOperatorExpression(prop, "[0,1]", "in", EnumOptions()).Build();
        var data    = new[]
        {
            new Stub { Status = StubStatus.Pending  },   // 0
            new Stub { Status = StubStatus.Active   },   // 1
            new Stub { Status = StubStatus.Deleted  }    // 2
        };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Equal(2, matched.Count);
        Assert.All(matched, s => Assert.NotEqual(StubStatus.Deleted, s.Status));
    }

    [Fact]
    public void Build_Nin_OnEnum_AcceptsStringNames()
    {
        // filter[status][nin]=["Deleted"] — exclude by string enum name
        var prop    = Prop<Stub>("Status");
        var result  = new OtterApiFilterOperatorExpression(prop, "[\"Deleted\"]", "nin", EnumOptions()).Build();
        var data    = new[]
        {
            new Stub { Status = StubStatus.Active },
            new Stub { Status = StubStatus.Deleted },
            new Stub { Status = StubStatus.Pending }
        };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Equal(2, matched.Count);
        Assert.All(matched, s => Assert.NotEqual(StubStatus.Deleted, s.Status));
    }

    // ── Operator is case-insensitive ──────────────────────────────────────────

    [Theory]
    [InlineData("EQ")]
    [InlineData("Eq")]
    public void Build_Operator_IsCaseInsensitive(string op)
    {
        var prop    = Prop<Stub>("Age");
        var result  = new OtterApiFilterOperatorExpression(prop, "5", op).Build();
        var data    = new[] { new Stub { Age = 5 }, new Stub { Age = 6 } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal(5, matched[0].Age);
    }

    // ── Predicate is always non-null for valid operators ──────────────────────

    [Fact]
    public void Build_ReturnsPredicate_NotNull()
    {
        var prop   = Prop<Stub>("Age");
        var result = new OtterApiFilterOperatorExpression(prop, "7", "eq").Build();

        Assert.NotNull(result.Predicate);
    }

    // ── Unsupported type+operator combos throw ────────────────────────────────

    [Fact]
    public void Build_Like_OnInt_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Age");
        var ex = Assert.Throws<OtterApiException>(
            () => new OtterApiFilterOperatorExpression(prop, "5", "like").Build());
        Assert.Equal("INVALID_FILTER_OPERATOR", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Build_Lt_OnString_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Name");
        var ex = Assert.Throws<OtterApiException>(
            () => new OtterApiFilterOperatorExpression(prop, "x", "lt").Build());
        Assert.Equal("INVALID_FILTER_OPERATOR", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Build_Lt_OnGuid_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Token");
        var ex = Assert.Throws<OtterApiException>(
            () => new OtterApiFilterOperatorExpression(prop, Guid.NewGuid().ToString(), "lt").Build());
        Assert.Equal("INVALID_FILTER_OPERATOR", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Build_Like_OnGuid_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Token");
        var ex = Assert.Throws<OtterApiException>(
            () => new OtterApiFilterOperatorExpression(prop, "abc", "like").Build());
        Assert.Equal("INVALID_FILTER_OPERATOR", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    // ── Unknown operator throws ───────────────────────────────────────────────

    [Fact]
    public void Build_UnknownOperator_ThrowsNotSupported()
    {
        var prop = Prop<Stub>("Age");
        var ex = Assert.Throws<OtterApiException>(
            () => new OtterApiFilterOperatorExpression(prop, "5", "between").Build());
        Assert.Equal("INVALID_FILTER_OPERATOR", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    // ── Nullable value types ──────────────────────────────────────────────────

    [Fact]
    public void Build_Eq_OnNullableInt_FiltersCorrectly()
    {
        var prop    = Prop<Stub>("NullableAge");
        var result  = new OtterApiFilterOperatorExpression(prop, "10", "eq").Build();
        var data    = new[] { new Stub { NullableAge = 10 }, new Stub { NullableAge = 20 } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal(10, matched[0].NullableAge);
    }

    [Fact]
    public void Build_Gt_OnNullableInt_FiltersCorrectly()
    {
        var prop    = Prop<Stub>("NullableAge");
        var result  = new OtterApiFilterOperatorExpression(prop, "5", "gt").Build();
        var data    = new[] { new Stub { NullableAge = 10 }, new Stub { NullableAge = 3 } };
        var matched = Apply<Stub>(result.Predicate!, data);

        Assert.Single(matched);
        Assert.Equal(10, matched[0].NullableAge);
    }

    // ── Stub ─────────────────────────────────────────────────────────────────

    private enum StubStatus { Pending = 0, Active = 1, Deleted = 2 }

    private class Stub
    {
        public string     Name        { get; set; } = string.Empty;
        public int        Age         { get; set; }
        public Guid       Token       { get; set; }
        public int?       NullableAge { get; set; }
        public StubStatus Status      { get; set; }
    }
}
