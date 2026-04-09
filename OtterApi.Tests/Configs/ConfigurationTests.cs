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
    // IsOperatorSupported
    // ══════════════════════════════════════════════════════════════════════════

    // ── String operators ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("like")]
    [InlineData("nlike")]
    [InlineData("in")]
    [InlineData("nin")]
    public void IsOperatorSupported_ReturnsTrue_ForStringCompatibleOperators(string op)
    {
        Assert.True(typeof(string).IsOperatorSupported(op));
    }

    [Theory]
    [InlineData("lt")]
    [InlineData("lteq")]
    [InlineData("gt")]
    [InlineData("gteq")]
    public void IsOperatorSupported_ReturnsFalse_ForNumericOnlyOperators_OnString(string op)
    {
        Assert.False(typeof(string).IsOperatorSupported(op));
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
    public void IsOperatorSupported_ReturnsTrue_ForIntCompatibleOperators(string op)
    {
        Assert.True(typeof(int).IsOperatorSupported(op));
    }

    [Theory]
    [InlineData("like")]
    [InlineData("nlike")]
    public void IsOperatorSupported_ReturnsFalse_ForStringOnlyOperators_OnInt(string op)
    {
        Assert.False(typeof(int).IsOperatorSupported(op));
    }

    // ── Guid operators ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("in")]
    [InlineData("nin")]
    public void IsOperatorSupported_ReturnsTrue_ForGuidCompatibleOperators(string op)
    {
        Assert.True(typeof(Guid).IsOperatorSupported(op));
    }

    [Theory]
    [InlineData("lt")]
    [InlineData("lteq")]
    [InlineData("gt")]
    [InlineData("gteq")]
    [InlineData("like")]
    [InlineData("nlike")]
    public void IsOperatorSupported_ReturnsFalse_ForUnsupportedOperators_OnGuid(string op)
    {
        Assert.False(typeof(Guid).IsOperatorSupported(op));
    }

    // ── Nullable type unwrapping ──────────────────────────────────────────────

    [Theory]
    [InlineData("eq")]
    [InlineData("gt")]
    [InlineData("lt")]
    public void IsOperatorSupported_UnwrapsNullableType_BeforeChecking(string op)
    {
        // int? should behave exactly like int
        Assert.True(typeof(int?).IsOperatorSupported(op));
    }

    [Fact]
    public void IsOperatorSupported_NullableGuid_BehavesLikeGuid()
    {
        Assert.True(typeof(Guid?).IsOperatorSupported("eq"));
        Assert.False(typeof(Guid?).IsOperatorSupported("lt"));
    }

    // ── Case-insensitive operator name matching ───────────────────────────────

    [Theory]
    [InlineData("EQ")]
    [InlineData("Eq")]
    [InlineData("LIKE")]
    public void IsOperatorSupported_Operator_IsCaseInsensitive(string op)
    {
        Assert.True(typeof(string).IsOperatorSupported(op));
    }

    // ── Unknown operator ──────────────────────────────────────────────────────

    [Fact]
    public void IsOperatorSupported_ReturnsFalse_ForUnknownOperator()
    {
        Assert.False(typeof(int).IsOperatorSupported("between"));
        Assert.False(typeof(string).IsOperatorSupported("startswith"));
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
}
