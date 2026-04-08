using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OtterApi.Enums;

namespace OtterApi.Models;

public class OtterApiEntity
{
    public string Route { get; set; }

    public string GetPolicy { get; set; }

    public string PostPolicy { get; set; }

    public string PutPolicy { get; set; }

    public string DeletePolicy { get; set; }

    public string EntityPolicy { get; set; }

    public bool Authorize { get; set; }

    public PropertyInfo DbSet { get; set; }

    public Type EntityType { get; set; }

    public List<PropertyInfo> Properties { get; set; }

    public List<PropertyInfo> NavigationProperties { get; set; }

    public PropertyInfo Id { get; set; }

    public Type DbContextType { get; set; }

    public bool ExposePagedResult { get; set; }

    public OtterApiCrudOperation AllowedOperations { get; set; } = OtterApiCrudOperation.All;

    /// <summary>
    /// Per-entity query filters applied to every GET request (list, count, pagedresult, by-Id).
    /// Each entry is a typed closure: (IQueryable untyped) → IQueryable with .Where applied.
    /// Multiple filters are chained — all must pass (AND semantics).
    /// </summary>
    public List<Func<IQueryable, IQueryable>> QueryFilters { get; set; } = [];

    /// <summary>
    /// Per-request scoped query filters resolved at runtime via IServiceProvider.
    /// Used for dynamic filtering dependent on the current HTTP context
    /// (e.g. userId or tenantId from the JWT token).
    /// Requires IHttpContextAccessor to be registered: services.AddHttpContextAccessor().
    /// </summary>
    public List<Func<IServiceProvider, Func<IQueryable, IQueryable>>> ScopedQueryFilterFactories { get; set; } = [];

    /// <summary>
    /// Named custom GET routes registered via <c>.WithCustomRoute(...)</c>.
    /// Each route is a pre-configured preset exposed at <c>{entityRoute}/{slug}</c>.
    /// </summary>
    public List<OtterApiCustomRoute> CustomRoutes { get; set; } = [];

    /// <summary>Handlers invoked before SaveChangesAsync. Multiple handlers run in registration order.</summary>
    public List<Func<DbContext, object, object?, OtterApiCrudOperation, Task>> PreSaveHandlers { get; set; } = [];

    /// <summary>Handlers invoked after SaveChangesAsync. Multiple handlers run in registration order.</summary>
    public List<Func<DbContext, object, object?, OtterApiCrudOperation, Task>> PostSaveHandlers { get; set; } = [];

    // ── Typed delegates — compiled once at startup, replace dynamic dispatch on every request ──

    /// <summary>
    /// Finds an entity by primary key. Equivalent to DbSet&lt;T&gt;.FindAsync(id).
    /// Compiled at startup from the generic T — no DLR overhead per request.
    /// </summary>
    public Func<DbContext, object, Task<object?>> FindByIdAsync { get; set; } = null!;

    /// <summary>
    /// Wraps IQueryable&lt;T&gt;.AsNoTracking() on the untyped IQueryable.
    /// Compiled at startup — no DLR overhead per request.
    /// </summary>
    public Func<IQueryable, IQueryable> AsNoTracking { get; set; } = null!;

    /// <summary>
    /// Wraps IQueryable&lt;T&gt;.CountAsync(ct) on the untyped IQueryable.
    /// Compiled at startup — no DLR overhead per request.
    /// </summary>
    public Func<IQueryable, CancellationToken, Task<int>> CountAsync { get; set; } = null!;

    /// <summary>
    /// Wraps IQueryable&lt;T&gt;.Include(navigationPropertyPath) on the untyped IQueryable.
    /// Compiled at startup — no DLR overhead per request.
    /// </summary>
    public Func<IQueryable, string, IQueryable> Include { get; set; } = null!;

    /// <summary>
    /// Returns the typed DbSet from the DbContext as an untyped IQueryable.
    /// Compiled at startup via Expression Tree — no reflection overhead per request.
    /// </summary>
    public Func<DbContext, IQueryable> GetDbSet { get; set; } = null!;

    /// <summary>
    /// Materializes an untyped IQueryable to <c>List&lt;object&gt;</c> via EF Core's ToListAsync.
    /// Compiled at startup from T — avoids reflection and Dynamic.Core per request.
    /// </summary>
    public Func<IQueryable, CancellationToken, Task<List<object>>> ToListAsync { get; set; } = null!;

    /// <summary>
    /// Filters the IQueryable to rows where the primary key equals the supplied (already-converted) id value.
    /// Compiled at startup from T — builds a typed Expression Tree on each call, no string parsing.
    /// </summary>
    public Func<IQueryable, object, IQueryable> WhereId { get; set; } = null!;

    /// <summary>
    /// Applies ORDER BY Id DESC to the IQueryable.
    /// Compiled at startup from T — no reflection or string parsing per request.
    /// </summary>
    public Func<IQueryable, IQueryable> OrderByIdDesc { get; set; } = null!;
}