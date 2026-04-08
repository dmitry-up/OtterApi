using OtterApi.Expressions;
using Xunit;

namespace OtterApi.Tests.Expressions;

/// <summary>
/// Contract: OtterApiSortOtterApiExpression converts a user-supplied sort direction token
/// into a Dynamic LINQ ORDER BY fragment.
/// </summary>
public class SortExpressionTests
{
    // ── Descending aliases ────────────────────────────────────────────────────

    [Theory]
    [InlineData("desc")]
    [InlineData("DESC")]
    [InlineData("Desc")]
    public void Build_ReturnsDescFragment_WhenSortOrderIsDesc(string sortOrder)
    {
        var expr = new OtterApiSortOtterApiExpression("Price", sortOrder);
        Assert.Equal("Price desc", expr.Build());
    }

    [Theory]
    [InlineData("1")]
    public void Build_ReturnsDescFragment_WhenSortOrderIs1(string sortOrder)
    {
        var expr = new OtterApiSortOtterApiExpression("Price", sortOrder);
        Assert.Equal("Price desc", expr.Build());
    }

    [Theory]
    [InlineData("descending")]
    [InlineData("DESCENDING")]
    public void Build_ReturnsDescFragment_WhenSortOrderIsDescending(string sortOrder)
    {
        var expr = new OtterApiSortOtterApiExpression("Price", sortOrder);
        Assert.Equal("Price desc", expr.Build());
    }

    // ── Ascending (default) ───────────────────────────────────────────────────

    [Theory]
    [InlineData("asc")]
    [InlineData("ASC")]
    [InlineData("ascending")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("random")]
    public void Build_ReturnsAscFragment_ForAnyNonDescValue(string sortOrder)
    {
        var expr = new OtterApiSortOtterApiExpression("Name", sortOrder);
        Assert.Equal("Name asc", expr.Build());
    }

    // ── Property name is preserved exactly ───────────────────────────────────

    [Fact]
    public void Build_PreservesPropertyNameCasing()
    {
        var expr = new OtterApiSortOtterApiExpression("CreatedAt", "asc");
        Assert.StartsWith("CreatedAt", expr.Build());
    }

    // ── Architectural concern: "2" is NOT treated as descending ──────────────
    // This test documents current behaviour. If the contract changes to accept
    // any non-zero integer as "desc", update it accordingly.
    [Fact]
    public void Build_Returns_Asc_For_NonOne_Integer()
    {
        var expr = new OtterApiSortOtterApiExpression("Price", "2");
        Assert.Equal("Price asc", expr.Build());
    }
}

