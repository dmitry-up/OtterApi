using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OtterApi.Expressions;
using Xunit;

namespace OtterApi.Tests.Expressions;

/// <summary>
/// Contract: OtterApiPagingOtterApiExpression maps the "page" / "pagesize" query-string
/// parameters to Skip / Take / Page integers used by the controller.
/// </summary>
public class PagingExpressionTests
{
    private static IQueryCollection Query(Dictionary<string, string> pairs)
        => new QueryCollection(pairs.ToDictionary(p => p.Key, p => new StringValues(p.Value)));

    // ── No paging parameters → sensible defaults ──────────────────────────────

    [Fact]
    public void Build_ReturnsDefaults_WhenNoPagingParams()
    {
        var result = new OtterApiPagingOtterApiExpression(Query([]), "page").Build();

        Assert.Equal(0, result.Take);
        Assert.Equal(0, result.Skip);
        Assert.Equal(1, result.Page);  // default page is 1
    }

    // ── Normal paging ─────────────────────────────────────────────────────────

    [Fact]
    public void Build_CalculatesSkip_ForPage2WithSize10()
    {
        var qs = Query(new() { ["page"] = "2", ["pagesize"] = "10" });
        var result = new OtterApiPagingOtterApiExpression(qs, "page").Build();

        Assert.Equal(10, result.Take);
        Assert.Equal(10, result.Skip);   // (2-1)*10
        Assert.Equal(2,  result.Page);
    }

    [Fact]
    public void Build_CalculatesSkip_ForPage3WithSize5()
    {
        var qs = Query(new() { ["page"] = "3", ["pagesize"] = "5" });
        var result = new OtterApiPagingOtterApiExpression(qs, "page").Build();

        Assert.Equal(5,  result.Take);
        Assert.Equal(10, result.Skip);   // (3-1)*5
        Assert.Equal(3,  result.Page);
    }

    // ── First page ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_SkipIsZero_ForPage1()
    {
        var qs = Query(new() { ["page"] = "1", ["pagesize"] = "20" });
        var result = new OtterApiPagingOtterApiExpression(qs, "page").Build();

        Assert.Equal(0,  result.Skip);
        Assert.Equal(20, result.Take);
        Assert.Equal(1,  result.Page);
    }

    // ── Only page size given (no page number) ────────────────────────────────

    [Fact]
    public void Build_UsesDefaultPage1_WhenOnlyPageSizeProvided()
    {
        var qs = Query(new() { ["pagesize"] = "15" });
        var result = new OtterApiPagingOtterApiExpression(qs, "page").Build();

        Assert.Equal(15, result.Take);
        Assert.Equal(0,  result.Skip);   // page defaults to 1 → (1-1)*15 = 0
        Assert.Equal(1,  result.Page);
    }

    // ── Non-numeric values are silently ignored (TryParse) ───────────────────

    [Fact]
    public void Build_IgnoresNonNumericPageParam()
    {
        var qs = Query(new() { ["page"] = "abc", ["pagesize"] = "10" });
        var result = new OtterApiPagingOtterApiExpression(qs, "page").Build();

        // TryParse fails → page stays at default 1
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public void Build_IgnoresNonNumericPageSizeParam()
    {
        var qs = Query(new() { ["page"] = "2", ["pagesize"] = "xyz" });
        var result = new OtterApiPagingOtterApiExpression(qs, "page").Build();

        // TryParse fails → pageSize stays at 0, skip = (2-1)*0 = 0
        Assert.Equal(0, result.Take);
        Assert.Equal(0, result.Skip);
    }

    // ── Case-insensitive prefix matching ─────────────────────────────────────

    [Fact]
    public void Build_IsKeyComparison_CaseInsensitive()
    {
        var qs = Query(new() { ["PAGE"] = "2", ["PAGESIZE"] = "5" });
        var result = new OtterApiPagingOtterApiExpression(qs, "page").Build();

        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.Take);
    }
}

