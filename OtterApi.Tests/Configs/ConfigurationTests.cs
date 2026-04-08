using OtterApi.Configs;
using Xunit;

namespace OtterApi.Tests.Configs;

/// <summary>
/// Contract: OtterApiConfiguration extension methods correctly classify
/// types and operators for filter support.
/// </summary>
public class ConfigurationTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // IsTypeSupported
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(double))]
    [InlineData(typeof(float))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(Guid))]
    public void IsTypeSupported_ReturnsTrue_ForSupportedTypes(Type type)
    {
        Assert.True(type.IsTypeSupported());
    }

    [Theory]
    [InlineData(typeof(List<string>))]
    [InlineData(typeof(object))]
    public void IsTypeSupported_ReturnsFalse_ForComplexTypes(Type type)
    {
        Assert.False(type.IsTypeSupported());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // IsOperatorSuported  (note: intentional typo in the library method name)
    // ══════════════════════════════════════════════════════════════════════════

    // ── String operators ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("like")]
    [InlineData("nlike")]
    [InlineData("in")]
    [InlineData("nin")]
    public void IsOperatorSuported_ReturnsTrue_ForStringCompatibleOperators(string op)
    {
        Assert.True(typeof(string).IsOperatorSuported(op));
    }

    [Theory]
    [InlineData("lt")]
    [InlineData("lteq")]
    [InlineData("gt")]
    [InlineData("gteq")]
    public void IsOperatorSuported_ReturnsFalse_ForNumericOnlyOperators_OnString(string op)
    {
        Assert.False(typeof(string).IsOperatorSuported(op));
    }

    // ── Numeric value-type operators ──────────────────────────────────────────

    [Theory]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("lt")]
    [InlineData("lteq")]
    [InlineData("gt")]
    [InlineData("gteq")]
    [InlineData("in")]
    [InlineData("nin")]
    public void IsOperatorSuported_ReturnsTrue_ForIntCompatibleOperators(string op)
    {
        Assert.True(typeof(int).IsOperatorSuported(op));
    }

    [Theory]
    [InlineData("like")]
    [InlineData("nlike")]
    public void IsOperatorSuported_ReturnsFalse_ForStringOnlyOperators_OnInt(string op)
    {
        Assert.False(typeof(int).IsOperatorSuported(op));
    }

    // ── Guid operators ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("in")]
    [InlineData("nin")]
    public void IsOperatorSuported_ReturnsTrue_ForGuidCompatibleOperators(string op)
    {
        Assert.True(typeof(Guid).IsOperatorSuported(op));
    }

    [Theory]
    [InlineData("lt")]
    [InlineData("lteq")]
    [InlineData("gt")]
    [InlineData("gteq")]
    [InlineData("like")]
    [InlineData("nlike")]
    public void IsOperatorSuported_ReturnsFalse_ForUnsupportedOperators_OnGuid(string op)
    {
        Assert.False(typeof(Guid).IsOperatorSuported(op));
    }

    // ── Nullable type unwrapping ──────────────────────────────────────────────

    [Theory]
    [InlineData("eq")]
    [InlineData("gt")]
    [InlineData("lt")]
    public void IsOperatorSuported_UnwrapsNullableType_BeforeChecking(string op)
    {
        // int? should behave exactly like int
        Assert.True(typeof(int?).IsOperatorSuported(op));
    }

    [Fact]
    public void IsOperatorSuported_NullableGuid_BehavesLikeGuid()
    {
        Assert.True(typeof(Guid?).IsOperatorSuported("eq"));
        Assert.False(typeof(Guid?).IsOperatorSuported("lt"));
    }

    // ── Case-insensitive operator name matching ───────────────────────────────

    [Theory]
    [InlineData("EQ")]
    [InlineData("Eq")]
    [InlineData("LIKE")]
    public void IsOperatorSuported_Operator_IsCaseInsensitive(string op)
    {
        Assert.True(typeof(string).IsOperatorSuported(op));
    }

    // ── Unknown operator ──────────────────────────────────────────────────────

    [Fact]
    public void IsOperatorSuported_ReturnsFalse_ForUnknownOperator()
    {
        Assert.False(typeof(int).IsOperatorSuported("between"));
        Assert.False(typeof(string).IsOperatorSuported("startswith"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Operators list – structural contract
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Operators_ContainsAllExpectedOperatorNames()
    {
        var names = OtterApiConfiguration.Operators.Select(o => o.Name).ToHashSet();

        foreach (var expected in new[] { "eq", "neq", "like", "nlike", "lt", "lteq", "gt", "gteq", "in", "nin" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void Operators_AllHaveNonEmptyExpression()
    {
        foreach (var op in OtterApiConfiguration.Operators)
            Assert.False(string.IsNullOrWhiteSpace(op.Expression));
    }

    [Fact]
    public void Operators_AllExpressionsContainPropertyNamePlaceholder()
    {
        // Every filter expression must reference {propertyName} so the builder can substitute it
        foreach (var op in OtterApiConfiguration.Operators)
            Assert.True(op.Expression.Contains("{propertyName}"),
                $"Operator '{op.Name}' expression is missing {{propertyName}} placeholder.");
    }

    [Fact]
    public void Operators_AllExpressionsContainIndexPlaceholder()
    {
        foreach (var op in OtterApiConfiguration.Operators)
            Assert.True(op.Expression.Contains("{index}"),
                $"Operator '{op.Name}' expression is missing {{index}} placeholder.");
    }
}


