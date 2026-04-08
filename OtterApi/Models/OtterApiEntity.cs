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
}