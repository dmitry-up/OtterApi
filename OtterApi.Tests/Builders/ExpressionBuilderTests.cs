using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OtterApi.Builders;
using OtterApi.Models;
using Xunit;

namespace OtterApi.Tests.Builders;

/// <summary>
/// Contract: OtterApiExpressionBuilder translates query-string parameters
/// into filter / sort / paging / include descriptors consumed by the controller.
/// </summary>
public class ExpressionBuilderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IQueryCollection Query(Dictionary<string, string> pairs) =>
        new QueryCollection(pairs.ToDictionary(k => k.Key, k => new StringValues(k.Value)));

    /// <summary>Entity that exposes Name (string), Age (int) as filterable props
    /// and Orders (object) as a navigation property.</summary>
    private static OtterApiEntity BuildEntity()
    {
        return new OtterApiEntity
        {
            Properties = typeof(ProductStub)
                .GetProperties()
                .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(int))
                .ToList(),

            NavigationProperties = typeof(ProductStub)
                .GetProperties()
                .Where(p => p.PropertyType != typeof(string) && p.PropertyType != typeof(int))
                .ToList(),
        };
    }

    private class ProductStub
    {
        public int    Id    { get; set; }
        public string Name  { get; set; } = string.Empty;
        public int    Age   { get; set; }
        public object? Orders { get; set; }
    }

    // ── BuildFilterResult – equality (no operator) ────────────────────────────

    [Fact]
    public void BuildFilterResult_EqualityFilter_ForKnownProperty()
    {
        var qs     = Query(new() { ["filter[Name]"] = "Alice" });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.Equal("Name == @0", result.Filter);
        Assert.Single(result.Values);
        Assert.Equal("Alice", result.Values[0]);
    }

    [Fact]
    public void BuildFilterResult_IsEmpty_ForUnknownProperty()
    {
        var qs     = Query(new() { ["filter[NonExistent]"] = "xyz" });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.Null(result.Filter);
        Assert.Null(result.Values);
    }

    // ── BuildFilterResult – with operator ────────────────────────────────────

    [Fact]
    public void BuildFilterResult_OperatorFilter_Like_OnString()
    {
        var qs     = Query(new() { ["filter[Name][like]"] = "ali" });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.Equal("Name.Contains(@0)", result.Filter);
    }

    [Fact]
    public void BuildFilterResult_OperatorFilter_Gt_OnInt()
    {
        var qs     = Query(new() { ["filter[Age][gt]"] = "18" });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.Equal("Age > @0", result.Filter);
        Assert.Equal(18, result.Values![0]);
    }

    // ── BuildFilterResult – join operator ────────────────────────────────────

    [Fact]
    public void BuildFilterResult_DefaultJoin_IsAnd()
    {
        var qs = Query(new()
        {
            ["filter[Name]"] = "Alice",
            ["filter[Age]"]  = "30"
        });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.Contains(" && ", result.Filter);
    }

    [Fact]
    public void BuildFilterResult_OrOperatorParam_ChangesJoinToOr()
    {
        var qs = Query(new()
        {
            ["operator"]     = "or",
            ["filter[Name]"] = "Alice",
            ["filter[Age]"]  = "30"
        });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.Contains(" || ", result.Filter);
        Assert.DoesNotContain(" && ", result.Filter);
    }

    [Fact]
    public void BuildFilterResult_OrOperatorParam_IsCaseInsensitive()
    {
        var qs = Query(new()
        {
            ["operator"]     = "OR",
            ["filter[Name]"] = "Alice",
            ["filter[Age]"]  = "30"
        });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.Contains(" || ", result.Filter);
    }

    // ── BuildFilterResult – multiple filters share index ─────────────────────

    [Fact]
    public void BuildFilterResult_MultipleFilters_HaveMonotonicallyIncreasingIndexes()
    {
        var qs = Query(new()
        {
            ["filter[Name]"] = "Alice",
            ["filter[Age]"]  = "25"
        });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        // Filter must reference @0 and @1
        Assert.Contains("@0", result.Filter);
        Assert.Contains("@1", result.Filter);
        Assert.Equal(2, result.Values!.Length);
    }

    // ── BuildFilterResult – no filter params ─────────────────────────────────

    [Fact]
    public void BuildFilterResult_ReturnsEmpty_WhenNoFilterParams()
    {
        var result = new OtterApiExpressionBuilder(Query([]), BuildEntity()).BuildFilterResult();

        Assert.Null(result.Filter);
        Assert.Null(result.Values);
    }

    // ── BuildSortResult ───────────────────────────────────────────────────────

    [Fact]
    public void BuildSortResult_ReturnsSortFragment_ForKnownProperty()
    {
        var qs     = Query(new() { ["sort[Name]"] = "asc" });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildSortResult();

        Assert.Equal("Name asc", result);
    }

    [Fact]
    public void BuildSortResult_ReturnsNull_WhenNoSortParams()
    {
        var result = new OtterApiExpressionBuilder(Query([]), BuildEntity()).BuildSortResult();

        Assert.Null(result);
    }

    [Fact]
    public void BuildSortResult_IgnoresUnknownProperty()
    {
        var qs     = Query(new() { ["sort[NonExistent]"] = "asc" });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildSortResult();

        Assert.Null(result);
    }

    [Fact]
    public void BuildSortResult_JoinsMultipleSortFragmentsWithComma()
    {
        var qs = Query(new()
        {
            ["sort[Name]"] = "asc",
            ["sort[Age]"]  = "desc"
        });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildSortResult()!;

        Assert.Contains("Name asc",  result);
        Assert.Contains("Age desc",  result);
        Assert.Contains(", ",        result);
    }

    // ── BuildPagingResult ─────────────────────────────────────────────────────

    [Fact]
    public void BuildPagingResult_ReturnsPagingInfo_ForPageAndPageSize()
    {
        var qs     = Query(new() { ["page"] = "3", ["pagesize"] = "5" });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildPagingResult();

        Assert.Equal(3,  result.Page);
        Assert.Equal(5,  result.Take);
        Assert.Equal(10, result.Skip);
    }

    // ── BuildIncludeResult ────────────────────────────────────────────────────

    [Fact]
    public void BuildIncludeResult_ReturnsNavProps_ForValidInclude()
    {
        var qs     = Query(new() { ["include"] = "Orders" });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildIncludeResult();

        Assert.Equal(["Orders"], result);
    }

    [Fact]
    public void BuildIncludeResult_ReturnsEmpty_WhenNoIncludeParam()
    {
        var result = new OtterApiExpressionBuilder(Query([]), BuildEntity()).BuildIncludeResult();

        Assert.Empty(result);
    }

    [Fact]
    public void BuildIncludeResult_IgnoresScalarProperties()
    {
        // "Name" is a scalar property, not a navigation property → should be ignored
        var qs     = Query(new() { ["include"] = "Name,Orders" });
        var result = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildIncludeResult();

        Assert.DoesNotContain("Name", result);
        Assert.Contains("Orders", result);
    }
}

