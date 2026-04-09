using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OtterApi.Builders;
using OtterApi.Models;
using Xunit;

namespace OtterApi.Tests.Builders;

/// <summary>
/// Contract: OtterApiExpressionBuilder translates query-string parameters
/// into filter / sort / paging / include delegates consumed by the controller.
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

    /// <summary>Apply a filter delegate to an in-memory sequence and return typed results.</summary>
    private static List<ProductStub> ApplyFilter(Func<IQueryable, IQueryable> filter, IEnumerable<ProductStub> data) =>
        filter(data.AsQueryable()).Cast<ProductStub>().ToList();

    /// <summary>Apply a sort delegate to an in-memory sequence and return typed results.</summary>
    private static List<ProductStub> ApplySort(Func<IQueryable, IQueryable> sort, IEnumerable<ProductStub> data) =>
        sort(data.AsQueryable()).Cast<ProductStub>().ToList();

    // ── BuildFilterResult – equality (no operator) ────────────────────────────

    [Fact]
    public void BuildFilterResult_EqualityFilter_ForKnownProperty()
    {
        var qs     = Query(new() { ["filter[Name]"] = "Alice" });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data    = new[] { new ProductStub { Name = "Alice" }, new ProductStub { Name = "Bob" } };
        var matched = ApplyFilter(filter!, data);

        Assert.Single(matched);
        Assert.Equal("Alice", matched[0].Name);
    }

    [Fact]
    public void BuildFilterResult_IsNull_ForUnknownProperty()
    {
        var qs     = Query(new() { ["filter[NonExistent]"] = "xyz" });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.Null(filter);
    }

    // ── BuildFilterResult – with operator ────────────────────────────────────

    [Fact]
    public void BuildFilterResult_OperatorFilter_Like_OnString()
    {
        var qs     = Query(new() { ["filter[Name][like]"] = "ali" });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data    = new[] { new ProductStub { Name = "alice" }, new ProductStub { Name = "Bob" } };
        var matched = ApplyFilter(filter!, data);

        Assert.Single(matched);
        Assert.Contains("ali", matched[0].Name);
    }

    [Fact]
    public void BuildFilterResult_OperatorFilter_Gt_OnInt()
    {
        var qs     = Query(new() { ["filter[Age][gt]"] = "18" });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data    = new[] { new ProductStub { Age = 19 }, new ProductStub { Age = 17 } };
        var matched = ApplyFilter(filter!, data);

        Assert.Single(matched);
        Assert.Equal(19, matched[0].Age);
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
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        // AND: only the item matching BOTH conditions should be returned
        var data = new[]
        {
            new ProductStub { Name = "Alice", Age = 30 },   // matches both
            new ProductStub { Name = "Alice", Age = 25 },   // Name only
            new ProductStub { Name = "Bob",   Age = 30 },   // Age only
        };
        var matched = ApplyFilter(filter!, data);

        Assert.Single(matched);
        Assert.Equal("Alice", matched[0].Name);
        Assert.Equal(30,      matched[0].Age);
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
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        // OR: items matching EITHER condition
        var data = new[]
        {
            new ProductStub { Name = "Alice", Age = 25 },   // Name matches
            new ProductStub { Name = "Bob",   Age = 30 },   // Age matches
            new ProductStub { Name = "Charlie", Age = 99 }, // neither
        };
        var matched = ApplyFilter(filter!, data);

        Assert.Equal(2, matched.Count);
        Assert.DoesNotContain(matched, m => m.Name == "Charlie");
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
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data = new[]
        {
            new ProductStub { Name = "Alice", Age = 25 },
            new ProductStub { Name = "Bob",   Age = 30 },
        };
        var matched = ApplyFilter(filter!, data);

        Assert.Equal(2, matched.Count);
    }

    // ── BuildFilterResult – multiple filters with AND semantics ──────────────

    [Fact]
    public void BuildFilterResult_MultipleFilters_AndSemanticsApplied()
    {
        var qs = Query(new()
        {
            ["filter[Name]"] = "Alice",
            ["filter[Age]"]  = "25"
        });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data = new[]
        {
            new ProductStub { Name = "Alice", Age = 25 },   // matches both → included
            new ProductStub { Name = "Alice", Age = 30 },   // Age wrong    → excluded
            new ProductStub { Name = "Bob",   Age = 25 },   // Name wrong   → excluded
        };
        var matched = ApplyFilter(filter!, data);

        Assert.Single(matched);
        Assert.Equal("Alice", matched[0].Name);
        Assert.Equal(25,      matched[0].Age);
    }

    // ── BuildFilterResult – no filter params ─────────────────────────────────

    [Fact]
    public void BuildFilterResult_ReturnsNull_WhenNoFilterParams()
    {
        var filter = new OtterApiExpressionBuilder(Query([]), BuildEntity()).BuildFilterResult();

        Assert.Null(filter);
    }

    // ── BuildFilterResult – grouped filters (new syntax) ─────────────────────

    [Fact]
    public void BuildFilterResult_GroupedSyntax_OrWithinSingleGroup()
    {
        // filter[0][Name]=Alice & filter[0][Age]=30 & operator[0]=or
        // → Name == "Alice" OR Age == 30
        var qs = Query(new()
        {
            ["filter[0][Name]"] = "Alice",
            ["filter[0][Age]"]  = "30",
            ["operator[0]"]     = "or"
        });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data = new[]
        {
            new ProductStub { Name = "Alice", Age = 25 },   // Name matches → in
            new ProductStub { Name = "Bob",   Age = 30 },   // Age matches  → in
            new ProductStub { Name = "Charlie", Age = 99 }, // neither      → out
        };
        var matched = ApplyFilter(filter!, data);

        Assert.Equal(2, matched.Count);
        Assert.DoesNotContain(matched, m => m.Name == "Charlie");
    }

    [Fact]
    public void BuildFilterResult_GroupedSyntax_TwoGroups_OrInFirstAndBetweenGroups()
    {
        // filter[0][Name][like]=ali & filter[0][Age][gt]=25 & operator[0]=or & filter[1][Id]=1
        // → (Name contains "ali" OR Age > 25) AND Id == 1
        var qs = Query(new()
        {
            ["filter[0][Name][like]"] = "ali",
            ["filter[0][Age][gt]"]    = "25",
            ["operator[0]"]           = "or",
            ["filter[1][Id]"]         = "1"
        });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data = new[]
        {
            new ProductStub { Id = 1, Name = "alice", Age = 20 }, // OR: name ✓, AND: id ✓ → in
            new ProductStub { Id = 1, Name = "Bob",   Age = 30 }, // OR: age  ✓, AND: id ✓ → in
            new ProductStub { Id = 2, Name = "alice", Age = 20 }, // OR: name ✓, AND: id ✗ → out
            new ProductStub { Id = 1, Name = "Bob",   Age = 10 }, // OR: none ✗            → out
        };
        var matched = ApplyFilter(filter!, data);

        Assert.Equal(2, matched.Count);
        Assert.All(matched, m => Assert.Equal(1, m.Id));
    }

    [Fact]
    public void BuildFilterResult_GroupedSyntax_TwoGroupsBothDefaultAnd()
    {
        // filter[0][Name]=Alice & filter[1][Age]=30 — no operators → all AND
        // → Name == "Alice" AND Age == 30
        var qs = Query(new()
        {
            ["filter[0][Name]"] = "Alice",
            ["filter[1][Age]"]  = "30"
        });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data = new[]
        {
            new ProductStub { Name = "Alice", Age = 30 },   // both → in
            new ProductStub { Name = "Alice", Age = 25 },   // Age wrong → out
            new ProductStub { Name = "Bob",   Age = 30 },   // Name wrong → out
        };
        var matched = ApplyFilter(filter!, data);

        Assert.Single(matched);
        Assert.Equal("Alice", matched[0].Name);
        Assert.Equal(30, matched[0].Age);
    }

    [Fact]
    public void BuildFilterResult_GroupedSyntax_OrOperatorIsCaseInsensitive()
    {
        var qs = Query(new()
        {
            ["filter[0][Name]"] = "Alice",
            ["filter[0][Age]"]  = "30",
            ["operator[0]"]     = "OR"   // uppercase
        });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data = new[]
        {
            new ProductStub { Name = "Alice", Age = 25 },
            new ProductStub { Name = "Bob",   Age = 30 },
        };
        // OR: both should match
        Assert.Equal(2, ApplyFilter(filter!, data).Count);
    }

    [Fact]
    public void BuildFilterResult_FlatAndGrouped_CanBeMixed()
    {
        // filter[Name]=Alice (flat → "default" group, AND)
        // filter[0][Age][gt]=25 & filter[0][Id][gt]=0 & operator[0]=or
        // → Name == "Alice"  AND  (Age > 25 OR Id > 0)
        var qs = Query(new()
        {
            ["filter[Name]"]       = "Alice",
            ["filter[0][Age][gt]"] = "25",
            ["filter[0][Id][gt]"]  = "0",
            ["operator[0]"]        = "or"
        });
        var filter = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildFilterResult();

        Assert.NotNull(filter);

        var data = new[]
        {
            new ProductStub { Id = 1, Name = "Alice",   Age = 30 }, // Name ✓, OR: age ✓  → in
            new ProductStub { Id = 1, Name = "Alice",   Age = 20 }, // Name ✓, OR: id  ✓  → in
            new ProductStub { Id = 1, Name = "Bob",     Age = 30 }, // Name ✗             → out
        };
        var matched = ApplyFilter(filter!, data);

        Assert.Equal(2, matched.Count);
        Assert.All(matched, m => Assert.Equal("Alice", m.Name));
    }

    // ── BuildSortResult ───────────────────────────────────────────────────────

    [Fact]
    public void BuildSortResult_ReturnsSortDelegate_ForKnownProperty()
    {
        var qs   = Query(new() { ["sort[Name]"] = "asc" });
        var sort = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildSortResult();

        Assert.NotNull(sort);

        var data   = new[] { new ProductStub { Name = "Beta" }, new ProductStub { Name = "Alpha" }, new ProductStub { Name = "Gamma" } };
        var sorted = ApplySort(sort!, data);

        Assert.Equal(["Alpha", "Beta", "Gamma"], sorted.Select(s => s.Name).ToList());
    }

    [Fact]
    public void BuildSortResult_ReturnsNull_WhenNoSortParams()
    {
        var sort = new OtterApiExpressionBuilder(Query([]), BuildEntity()).BuildSortResult();

        Assert.Null(sort);
    }

    [Fact]
    public void BuildSortResult_IgnoresUnknownProperty()
    {
        var qs   = Query(new() { ["sort[NonExistent]"] = "asc" });
        var sort = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildSortResult();

        Assert.Null(sort);
    }

    [Fact]
    public void BuildSortResult_MultipleFields_AppliesBothSorts()
    {
        var qs   = Query(new() { ["sort[Name]"] = "asc", ["sort[Age]"] = "desc" });
        var sort = new OtterApiExpressionBuilder(qs, BuildEntity()).BuildSortResult();

        Assert.NotNull(sort);

        // Design: Alice appears twice (ages 30 and 25), Bob once.
        // Whether primary sort is Name or Age, the result is the same:
        //   Alice-30, Alice-25, Bob-20
        var data = new[]
        {
            new ProductStub { Name = "Alice", Age = 25 },
            new ProductStub { Name = "Bob",   Age = 20 },
            new ProductStub { Name = "Alice", Age = 30 },
        };
        var sorted = ApplySort(sort!, data);

        Assert.Equal(3, sorted.Count);
        // Primary Name asc: the two Alices come before Bob
        Assert.Equal("Alice", sorted[0].Name);
        Assert.Equal("Alice", sorted[1].Name);
        Assert.Equal("Bob",   sorted[2].Name);
        // Secondary Age desc within the Alice group
        Assert.Equal(30, sorted[0].Age);
        Assert.Equal(25, sorted[1].Age);
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

