using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OtterApi.Configs;
using OtterApi.Enums;
using OtterApi.Helpers;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Builders;

public class OtterApiEntityBuilder<T> : IOtterApiEntityBuilder where T : class
{
    private readonly string route;
    private bool authorize;
    private string? deletePolicy;
    private string? patchPolicy;
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

    public OtterApiEntityBuilder<T> WithPatchPolicy(string policy)
    {
        patchPolicy = policy;
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
            Slug     = slug.Trim('/').ToLowerInvariant(),
            Sort     = sort,
            SortApply = sort != null ? q => OtterApiDynamicLinq.OrderBy(q, sort) : null,
            Take     = take,
            Single   = single
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

        // ── Compile GetDbSet delegate: (DbContext ctx) => (IQueryable)((TDbCtx)ctx).DbSetProp ──
        var ctxParam   = Expression.Parameter(typeof(DbContext), "ctx");
        var castCtx    = Expression.Convert(ctxParam, dbContextType);
        var propAccess = Expression.Property(castCtx, dbSetProperty);
        var castToQ    = Expression.Convert(propAccess, typeof(IQueryable));
        var getDbSet   = Expression.Lambda<Func<DbContext, IQueryable>>(castToQ, ctxParam).Compile();

        // ── Resolve primary key: [Key] attribute → "Id" convention → "{ClassName}Id" convention ──
        var idPropInfo =
            entityType.GetProperties().FirstOrDefault(p => p.IsDefined(typeof(KeyAttribute), false))
            ?? entityType.GetProperties().FirstOrDefault(p =>
                p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
            ?? entityType.GetProperties().FirstOrDefault(p =>
                p.Name.Equals($"{entityType.Name}Id", StringComparison.OrdinalIgnoreCase));

        // ── Compile WhereId and OrderByIdDesc delegates ───────────────────────

        Func<IQueryable, object, IQueryable> whereId = (q, _) => q; // no-op for keyless
        Func<IQueryable, IQueryable> orderByIdDesc   = q => q;      // no-op for keyless

        if (idPropInfo != null)
        {
            var keyType       = idPropInfo.PropertyType;
            var underlying    = Nullable.GetUnderlyingType(keyType) ?? keyType;
            var idPropCapture = idPropInfo;

            whereId = (q, id) =>
            {
                var p         = Expression.Parameter(typeof(T), "x");
                var propExpr  = Expression.Property(p, idPropCapture);
                var raw       = id.GetType() == underlying ? id : Convert.ChangeType(id, underlying);
                var constExpr = keyType != underlying
                    ? Expression.Convert(Expression.Constant(raw, underlying), keyType)
                    : (Expression)Expression.Constant(raw, keyType);
                var lambda    = Expression.Lambda<Func<T, bool>>(Expression.Equal(propExpr, constExpr), p);
                return ((IQueryable<T>)q).Where(lambda);
            };

            var odParam    = Expression.Parameter(typeof(T), "x");
            var odKeyExpr  = Expression.Lambda(Expression.Property(odParam, idPropInfo), odParam);
            var odMethod   = OtterApiDynamicLinq.QueryableOrderByDescending
                .MakeGenericMethod(typeof(T), keyType);
            orderByIdDesc  = q => (IQueryable)odMethod.Invoke(null, [q, odKeyExpr])!;
        }

        return new OtterApiEntity
        {
            Route = route,
            GetPolicy = getPolicy,
            PostPolicy = postPolicy,
            PutPolicy = putPolicy,
            DeletePolicy = deletePolicy,
            PatchPolicy = patchPolicy,
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
            Id = idPropInfo,
            PreSaveHandlers  = preSaveHandlers,
            PostSaveHandlers = postSaveHandlers,

            // ── Typed delegates compiled once from T ────────────────────────────
            FindByIdAsync = async (ctx, id, ct) => (object?)await ctx.Set<T>().FindAsync(new object?[] { id }, ct),
            AsNoTracking  = q => ((IQueryable<T>)q).AsNoTracking(),
            CountAsync    = (q, ct) => ((IQueryable<T>)q).CountAsync(ct),
            Include       = (q, nav) => ((IQueryable<T>)q).Include(nav),
            GetDbSet      = getDbSet,
            ToListAsync   = async (q, ct) =>
            {
                var typed = await ((IQueryable<T>)q).ToListAsync(ct);
                return typed.ConvertAll(static item => (object)item);
            },
            WhereId       = whereId,
            OrderByIdDesc = orderByIdDesc
        };
    }
}