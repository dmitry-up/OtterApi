using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OtterApi.Configs;
using OtterApi.Enums;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Builders;

public class OtterApiEntityBuilder<T> : IOtterApiEntityBuilder where T : class
{
    private readonly string route;
    private bool authorize;
    private string? deletePolicy;
    private string? entityPolicy;
    private bool exposePagedResult;
    private string? getPolicy;
    private string? postPolicy;
    private string? putPolicy;
    private OtterApiCrudOperation allowedOperations = OtterApiCrudOperation.All;
    private readonly List<Func<IQueryable, IQueryable>> queryFilters = [];
    private readonly List<Func<IServiceProvider, Func<IQueryable, IQueryable>>> scopedQueryFilterFactories = [];
    private readonly List<OtterApiCustomRoute> customRoutes = [];
    private readonly List<Func<DbContext, object, object?, OtterApiCrudOperation, Task>> preSaveHandlers = [];
    private readonly List<Func<DbContext, object, object?, OtterApiCrudOperation, Task>> postSaveHandlers = [];

    internal OtterApiEntityBuilder(string route)
    {
        this.route = route;
    }

    public OtterApiEntityBuilder<T> Authorize(bool authorize = true)
    {
        this.authorize = authorize;
        return this;
    }

    public OtterApiEntityBuilder<T> WithEntityPolicy(string policy)
    {
        entityPolicy = policy;
        return this;
    }

    public OtterApiEntityBuilder<T> WithGetPolicy(string policy)
    {
        getPolicy = policy;
        return this;
    }

    public OtterApiEntityBuilder<T> WithPostPolicy(string policy)
    {
        postPolicy = policy;
        return this;
    }

    public OtterApiEntityBuilder<T> WithPutPolicy(string policy)
    {
        putPolicy = policy;
        return this;
    }

    public OtterApiEntityBuilder<T> WithDeletePolicy(string policy)
    {
        deletePolicy = policy;
        return this;
    }

    public OtterApiEntityBuilder<T> ExposePagedResult(bool expose = true)
    {
        exposePagedResult = expose;
        return this;
    }

    public OtterApiEntityBuilder<T> Allow(OtterApiCrudOperation operations)
    {
        allowedOperations = operations;
        return this;
    }

    /// <summary>
    /// Adds a per-entity query filter applied to every GET request.
    /// The lambda must use only EF-translatable operations.
    /// Multiple calls chain filters with AND semantics.
    /// </summary>
    public OtterApiEntityBuilder<T> WithQueryFilter(Expression<Func<T, bool>> predicate)
    {
        queryFilters.Add(q => ((IQueryable<T>)q).Where(predicate));
        return this;
    }

    /// <summary>
    /// Adds a per-request scoped query filter resolved at runtime via IServiceProvider.
    /// Use this for dynamic filtering that depends on the current HTTP context
    /// (e.g. userId or tenantId from the JWT token).
    /// Requires IHttpContextAccessor: services.AddHttpContextAccessor().
    /// Multiple calls chain filters with AND semantics.
    /// </summary>
    public OtterApiEntityBuilder<T> WithScopedQueryFilter(
        Func<IServiceProvider, Expression<Func<T, bool>>> predicateFactory)
    {
        scopedQueryFilterFactories.Add(sp =>
        {
            var predicate = predicateFactory(sp);
            return q => ((IQueryable<T>)q).Where(predicate);
        });
        return this;
    }

    public OtterApiEntityBuilder<T> BeforeSave(Action<DbContext, T, T?, OtterApiCrudOperation> handler)
    {
        preSaveHandlers.Add((ctx, newEntity, originalEntity, op) =>
        {
            handler(ctx, (T)newEntity, originalEntity is T orig ? orig : default, op);
            return Task.CompletedTask;
        });
        return this;
    }

    public OtterApiEntityBuilder<T> BeforeSave(Func<DbContext, T, T?, OtterApiCrudOperation, Task> handler)
    {
        preSaveHandlers.Add((ctx, newEntity, originalEntity, op) =>
            handler(ctx, (T)newEntity, originalEntity is T orig ? orig : default, op));
        return this;
    }

    public OtterApiEntityBuilder<T> BeforeSave(IOtterApiBeforeSaveHandler<T> handler)
        => BeforeSave(handler.BeforeSaveAsync);

    public OtterApiEntityBuilder<T> AfterSave(Action<DbContext, T, T?, OtterApiCrudOperation> handler)
    {
        postSaveHandlers.Add((ctx, newEntity, originalEntity, op) =>
        {
            handler(ctx, (T)newEntity, originalEntity is T orig ? orig : default, op);
            return Task.CompletedTask;
        });
        return this;
    }

    public OtterApiEntityBuilder<T> AfterSave(Func<DbContext, T, T?, OtterApiCrudOperation, Task> handler)
    {
        postSaveHandlers.Add((ctx, newEntity, originalEntity, op) =>
            handler(ctx, (T)newEntity, originalEntity is T orig ? orig : default, op));
        return this;
    }

    public OtterApiEntityBuilder<T> AfterSave(IOtterApiAfterSaveHandler<T> handler)
        => AfterSave(handler.AfterSaveAsync);

    /// <summary>
    /// Registers a named custom GET route exposed at <c>{entityRoute}/{slug}</c>.
    /// The route applies its own filter (stacked on top of entity-level QueryFilters),
    /// an optional sort, and an optional Take limit.
    /// When <paramref name="single"/> is <c>true</c> the endpoint returns a single
    /// object (or 404); otherwise it returns an array.
    /// Multiple calls register independent routes — slugs must be unique and must not
    /// conflict with reserved paths (<c>count</c>, <c>pagedresult</c>).
    /// </summary>
    public OtterApiEntityBuilder<T> WithCustomRoute(
        string slug,
        Expression<Func<T, bool>>? filter = null,
        string? sort = null,
        int take = 0,
        bool single = false)
    {
        var cr = new OtterApiCustomRoute
        {
            Slug   = slug.Trim('/').ToLowerInvariant(),
            Sort   = sort,
            Take   = take,
            Single = single
        };

        if (filter != null)
            cr.Filters.Add(q => ((IQueryable<T>)q).Where(filter));

        customRoutes.Add(cr);
        return this;
    }

    public OtterApiEntity Build(Type dbContextType, OtterApiOptions options)
    {
        var dbSetProperty = dbContextType.GetProperties()
            .FirstOrDefault(p => p.PropertyType == typeof(DbSet<T>));

        if (dbSetProperty == null)
            throw new InvalidOperationException(
                $"No DbSet<{typeof(T).Name}> found in DbContext '{dbContextType.Name}'. " +
                $"Make sure the DbContext has a DbSet<{typeof(T).Name}> property.");

        var entityType = typeof(T);
        var route = new PathString(options.Path)
            .Add(this.route.StartsWith("/") ? this.route : $"/{this.route}");

        // ── Validate custom route slugs ────────────────────────────────────────
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "count", "pagedresult" };
        foreach (var cr in customRoutes)
        {
            if (reserved.Contains(cr.Slug))
                throw new InvalidOperationException(
                    $"Custom route slug '{cr.Slug}' on entity '{entityType.Name}' conflicts with " +
                    $"a reserved OtterApi path. Reserved slugs: {string.Join(", ", reserved)}.");
        }

        var duplicateSlugs = customRoutes
            .GroupBy(r => r.Slug, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateSlugs.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate custom route slugs on entity '{entityType.Name}': " +
                $"{string.Join(", ", duplicateSlugs)}.");

        return new OtterApiEntity
        {
            Route = route,
            GetPolicy = getPolicy,
            PostPolicy = postPolicy,
            PutPolicy = putPolicy,
            DeletePolicy = deletePolicy,
            EntityPolicy = entityPolicy,
            Authorize = authorize,
            DbSet = dbSetProperty,
            EntityType = entityType,
            DbContextType = dbContextType,
            ExposePagedResult = exposePagedResult,
            AllowedOperations = allowedOperations,
            QueryFilters = queryFilters,
            ScopedQueryFilterFactories = scopedQueryFilterFactories,
            CustomRoutes = customRoutes,
            Properties = entityType.GetProperties()
                .Where(x => x.PropertyType.IsTypeSupported()).ToList(),
            NavigationProperties = entityType.GetProperties()
                .Where(x => !x.PropertyType.IsTypeSupported()).ToList(),
            Id = entityType.GetProperties()
                .FirstOrDefault(x => x.IsDefined(typeof(KeyAttribute), false)),
            PreSaveHandlers  = preSaveHandlers,
            PostSaveHandlers = postSaveHandlers
        };
    }
}