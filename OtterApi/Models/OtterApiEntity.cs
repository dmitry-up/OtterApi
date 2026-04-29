using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OtterApi.Enums;

namespace OtterApi.Models;

public class OtterApiEntity
{
    public string Route { get; init; }

    public string GetPolicy { get; init; }

    public string PostPolicy { get; init; }

    public string PutPolicy { get; init; }

    public string DeletePolicy { get; init; }

    public string PatchPolicy { get; init; }

    public string EntityPolicy { get; init; }

    public bool Authorize { get; init; }

    public PropertyInfo DbSet { get; init; }

    public Type EntityType { get; init; }

    public List<PropertyInfo> Properties { get; init; }

    public List<PropertyInfo> NavigationProperties { get; init; }

    public PropertyInfo Id { get; init; }

    public Type DbContextType { get; init; }

    public bool ExposePagedResult { get; init; }

    public OtterApiCrudOperation AllowedOperations { get; init; } = OtterApiCrudOperation.All;

    /// <summary>
    /// Per-entity query filters applied to every GET request (list, count, pagedresult, by-Id).
    /// Each entry is a typed closure: (IQueryable untyped) → IQueryable with .Where applied.
    /// Multiple filters are chained — all must pass (AND semantics).
    /// </summary>
    public List<Func<IQueryable, IQueryable>> QueryFilters { get; init; } = [];

    /// <summary>
    /// Per-request scoped query filters resolved at runtime via IServiceProvider.
    /// Used for dynamic filtering dependent on the current HTTP context
    /// (e.g. userId or tenantId from the JWT token).
    /// Requires IHttpContextAccessor to be registered: services.AddHttpContextAccessor().
    /// </summary>
    public List<Func<IServiceProvider, Func<IQueryable, IQueryable>>> ScopedQueryFilterFactories { get; init; } = [];

    /// <summary>
    /// Named custom GET routes registered via <c>.WithCustomRoute(...)</c>.
    /// Each route is a pre-configured preset exposed at <c>{entityRoute}/{slug}</c>.
    /// </summary>
    public List<OtterApiCustomRoute> CustomRoutes { get; init; } = [];

    /// <summary>Handlers invoked before SaveChangesAsync. Multiple handlers run in registration order.</summary>
    public List<Func<DbContext, object, object?, OtterApiCrudOperation, Task>> PreSaveHandlers { get; init; } = [];

    /// <summary>Handlers invoked after SaveChangesAsync. Multiple handlers run in registration order.</summary>
    public List<Func<DbContext, object, object?, OtterApiCrudOperation, Task>> PostSaveHandlers { get; init; } = [];

    /// <summary><c>true</c> when soft-delete is enabled via <c>.WithSoftDelete(...)</c>.</summary>
    public bool IsSoftDelete { get; init; }

    // ── Typed delegates — compiled once at startup, replace dynamic dispatch on every request ──

    /// <summary>
    /// Finds an entity by primary key. Equivalent to DbSet&lt;T&gt;.FindAsync(id, ct).
    /// Compiled at startup from the generic T — no DLR overhead per request.
    /// </summary>
    public Func<DbContext, object, CancellationToken, Task<object?>> FindByIdAsync { get; init; } = null!;

    /// <summary>
    /// Wraps IQueryable&lt;T&gt;.AsNoTracking() on the untyped IQueryable.
    /// Compiled at startup — no DLR overhead per request.
    /// </summary>
    public Func<IQueryable, IQueryable> AsNoTracking { get; init; } = null!;

    /// <summary>
    /// Wraps IQueryable&lt;T&gt;.CountAsync(ct) on the untyped IQueryable.
    /// Compiled at startup — no DLR overhead per request.
    /// </summary>
    public Func<IQueryable, CancellationToken, Task<int>> CountAsync { get; init; } = null!;

    /// <summary>
    /// Wraps IQueryable&lt;T&gt;.Include(navigationPropertyPath) on the untyped IQueryable.
    /// Compiled at startup — no DLR overhead per request.
    /// </summary>
    public Func<IQueryable, string, IQueryable> Include { get; init; } = null!;

    /// <summary>
    /// Returns the typed DbSet from the DbContext as an untyped IQueryable.
    /// Compiled at startup via Expression Tree — no reflection overhead per request.
    /// </summary>
    public Func<DbContext, IQueryable> GetDbSet { get; init; } = null!;

    /// <summary>
    /// Materializes an untyped IQueryable to <c>List&lt;object&gt;</c> via EF Core's ToListAsync.
    /// Compiled at startup from T — avoids reflection and Dynamic.Core per request.
    /// </summary>
    public Func<IQueryable, CancellationToken, Task<List<object>>> ToListAsync { get; init; } = null!;

    /// <summary>
    /// Filters the IQueryable to rows where the primary key equals the supplied (already-converted) id value.
    /// Compiled at startup from T — builds a typed Expression Tree on each call, no string parsing.
    /// </summary>
    public Func<IQueryable, object, IQueryable> WhereId { get; init; } = null!;

    /// <summary>
    /// Applies ORDER BY Id DESC to the IQueryable.
    /// Compiled at startup from T — no reflection or string parsing per request.
    /// </summary>
    public Func<IQueryable, IQueryable> OrderByIdDesc { get; init; } = null!;

    /// <summary>
    /// Executes the delete action for this entity.
    /// Default: <c>dbContext.Remove(entity)</c> (hard delete).
    /// Replaced with a compiled property-setter when <c>.WithSoftDelete(...)</c> is configured.
    /// </summary>
    public Action<DbContext, object> DeleteApply { get; init; } = (ctx, e) => ctx.Remove(e);
}