namespace OtterApi.Models;

public class OtterApiRouteInfo
{
    public OtterApiEntity? Entity { get; set; }
    public string? Id { get; set; }

    /// <summary>
    /// Compiled filter delegate built by <see cref="OtterApi.Builders.OtterApiExpressionBuilder"/>.
    /// Null when no filter parameters were present in the query string.
    /// Applied as a single typed <c>IQueryable.Where(predicate)</c> — no string parsing at request time.
    /// </summary>
    public Func<IQueryable, IQueryable>? FilterApply { get; set; }

    /// <summary>
    /// Compiled sort delegate built by <see cref="OtterApi.Builders.OtterApiExpressionBuilder"/>.
    /// Null when no sort parameters were present in the query string.
    /// </summary>
    public Func<IQueryable, IQueryable>? SortApply { get; set; }

    public int Skip { get; set; }
    public int Take { get; set; }
    public int Page { get; set; }
    public bool IsCount { get; set; }
    public bool IsPageResult { get; set; }
    public List<string> IncludeExpression { get; set; } = [];

    /// <summary>
    /// Set when the request path matches a named custom route slug
    /// registered via <c>.WithCustomRoute(...)</c>.
    /// </summary>
    public OtterApiCustomRoute? CustomRoute { get; set; }
}