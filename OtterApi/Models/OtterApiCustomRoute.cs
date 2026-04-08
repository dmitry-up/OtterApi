namespace OtterApi.Models;

/// <summary>
/// Represents a named custom GET route registered on an entity via
/// <c>.WithCustomRoute(...)</c>. Exposed at <c>{entityRoute}/{slug}</c>.
/// </summary>
public class OtterApiCustomRoute
{
    /// <summary>
    /// URL segment appended to the entity base route, e.g. "last", "featured".
    /// Always stored lowercase without leading/trailing slashes.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Additional row filters applied on top of the entity-level QueryFilters.
    /// Chained in order — all must pass (AND semantics).
    /// </summary>
    public List<Func<IQueryable, IQueryable>> Filters { get; set; } = [];

    /// <summary>
    /// Optional sort expression (e.g. <c>"CreatedAt desc"</c>) for documentation / debugging.
    /// The compiled sort is stored in <see cref="SortApply"/>.
    /// </summary>
    public string? Sort { get; set; }

    /// <summary>
    /// Pre-compiled sort delegate built from <see cref="Sort"/> at entity registration time.
    /// Applied when no client sort is provided.
    /// </summary>
    public Func<IQueryable, IQueryable>? SortApply { get; set; }

    /// <summary>
    /// Maximum number of rows to return. 0 means no built-in limit
    /// (client skip/take and pagination still apply).
    /// </summary>
    public int Take { get; set; }

    /// <summary>
    /// When <c>true</c> returns the first matching object directly (T or 404).
    /// When <c>false</c> returns an array (possibly empty).
    /// </summary>
    public bool Single { get; set; }
}

