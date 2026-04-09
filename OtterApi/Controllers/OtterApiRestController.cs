using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using OtterApi.Converters;
using OtterApi.Enums;
using OtterApi.Exceptions;
using OtterApi.Helpers;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Controllers;

public class OtterApiRestController(
    DbContext dbContext,
    ActionContext actionContext,
    IObjectModelValidator objectModelValidator,
    IServiceProvider? serviceProvider = null,
    IOtterApiRegistry? registry = null)
    : IOtterApiRestController
{
    private const string KeylessError  = "Operation not allowed for keyless entities";
    private const string KeylessCode   = "KEYLESS_ENTITY";

    /// <summary>
    /// Fallback serialization options used when the controller is created without a registry
    /// (e.g. in unit tests). Equivalent to registry.SerializationOptions but created once statically.
    /// </summary>
    private static readonly JsonSerializerOptions FallbackSerializationOptions = BuildFallbackOptions();

    private static JsonSerializerOptions BuildFallbackOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        opts.Converters.Add(new OtterApiCaseInsensitiveEnumConverterFactory());
        return opts;
    }

    public async Task<ObjectResult> GetAsync(OtterApiRouteInfo otterApiRouteInfo, CancellationToken ct = default)
    {
        // ── Named custom route (e.g. GET /api/products/featured) ──────────────
        if (otterApiRouteInfo.CustomRoute != null)
            return await GetCustomRouteAsync(otterApiRouteInfo, ct);

        if (otterApiRouteInfo.Id != null)
        {
            if (otterApiRouteInfo.Entity.Id == null)
                throw new OtterApiException(KeylessCode, KeylessError, StatusCodes.Status405MethodNotAllowed);

            var idValue = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);

            // Fast path: no query filters → FindAsync
            if (otterApiRouteInfo.Entity.QueryFilters.Count == 0
                && otterApiRouteInfo.Entity.ScopedQueryFilterFactories.Count == 0)
            {
                var result = await otterApiRouteInfo.Entity.FindByIdAsync(dbContext, idValue, ct);
                return result != null ? GetOkObjectResult(result) : new NotFoundObjectResult(null);
            }

            // Query filters present → WhereId + filters so the filter is applied in the same SQL query.
            var filteredSet = otterApiRouteInfo.Entity.GetDbSet(dbContext);
            filteredSet = ApplyQueryFilters(filteredSet, otterApiRouteInfo.Entity);
            filteredSet = ApplyScopedQueryFilters(filteredSet, otterApiRouteInfo.Entity);
            filteredSet = otterApiRouteInfo.Entity.WhereId(filteredSet, idValue);
            var filteredResult = (await otterApiRouteInfo.Entity.ToListAsync(filteredSet, ct))
                .FirstOrDefault();
            return filteredResult != null ? GetOkObjectResult(filteredResult) : new NotFoundObjectResult(null);
        }

        var dbSet = otterApiRouteInfo.Entity.GetDbSet(dbContext);
        dbSet = ApplyQueryFilters(dbSet, otterApiRouteInfo.Entity);
        dbSet = ApplyScopedQueryFilters(dbSet, otterApiRouteInfo.Entity);

        foreach (var include in otterApiRouteInfo.IncludeExpression)
            dbSet = otterApiRouteInfo.Entity.Include(dbSet, include);

        if (otterApiRouteInfo.FilterApply != null)
            dbSet = otterApiRouteInfo.FilterApply(dbSet);

        dbSet = otterApiRouteInfo.SortApply != null
            ? otterApiRouteInfo.SortApply(dbSet)
            : ApplyDefaultSort(dbSet, otterApiRouteInfo);

        if (otterApiRouteInfo.IsCount)
            return GetOkObjectResult(await otterApiRouteInfo.Entity.CountAsync(dbSet, ct));

        if (otterApiRouteInfo.IsPageResult)
        {
            var pageSize = ClampPageSize(otterApiRouteInfo.Take == 0 ? 10 : otterApiRouteInfo.Take);
            var page     = otterApiRouteInfo.Page < 1 ? 1 : otterApiRouteInfo.Page;
            return GetOkObjectResult(await GetPagedResultAsync(otterApiRouteInfo.Entity, dbSet, page, pageSize, ct));
        }

        // Apply pagination. When the client provides no ?pagesize, ClampPageSize(0) returns
        // MaxPageSize (when configured) so unbounded queries against large tables are prevented.
        var effectiveTake = ClampPageSize(otterApiRouteInfo.Take);
        if (effectiveTake > 0)
            dbSet = OtterApiDynamicLinq.Take(OtterApiDynamicLinq.Skip(dbSet, otterApiRouteInfo.Skip), effectiveTake);

        return GetOkObjectResult(await otterApiRouteInfo.Entity.ToListAsync(dbSet, ct));
    }

    public async Task<ObjectResult> PostAsync(OtterApiRouteInfo otterApiRouteInfo, object entity, CancellationToken ct = default)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new OtterApiException(KeylessCode, KeylessError, StatusCodes.Status405MethodNotAllowed);

        if (!IsValid(entity))
            return new BadRequestObjectResult(actionContext.ModelState);

        // Run BeforeSave hooks while the entity is still detached — hooks see a clean ChangeTracker.
        // Add() is called only after all pre-save handlers succeed, so a hook that throws
        // (e.g. DUPLICATE_NAME check) never leaves a ghost entry in the tracker.
        foreach (var h in otterApiRouteInfo.Entity.PreSaveHandlers)
            await h(dbContext, entity, null, OtterApiCrudOperation.Post);
        dbContext.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        foreach (var h in otterApiRouteInfo.Entity.PostSaveHandlers)
            await h(dbContext, entity, null, OtterApiCrudOperation.Post);

        var newId = otterApiRouteInfo.Entity.Id.GetValue(entity);
        return new CreatedResult($"{otterApiRouteInfo.Entity.Route}/{newId}", entity);
    }

    public async Task<ObjectResult> PutAsync(OtterApiRouteInfo otterApiRouteInfo, object entity, CancellationToken ct = default)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new OtterApiException(KeylessCode, KeylessError, StatusCodes.Status405MethodNotAllowed);

        if (string.IsNullOrEmpty(otterApiRouteInfo.Id))
            return new BadRequestObjectResult("Id is required in the route for PUT operations");

        var objectId = otterApiRouteInfo.Entity.Id.GetValue(entity);
        var routeId  = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);

        if (!objectId!.Equals(routeId))
            return new BadRequestObjectResult("Id in route must match Id in the request body");

        if (!IsValid(entity))
            return new BadRequestObjectResult(actionContext.ModelState);

        var original = await LoadOriginalAsync(otterApiRouteInfo, ct);
        if (original == null)
            return new NotFoundObjectResult(null);

        dbContext.Entry(entity).State = EntityState.Modified;
        foreach (var h in otterApiRouteInfo.Entity.PreSaveHandlers)
            await h(dbContext, entity, original, OtterApiCrudOperation.Put);
        await dbContext.SaveChangesAsync(ct);
        foreach (var h in otterApiRouteInfo.Entity.PostSaveHandlers)
            await h(dbContext, entity, original, OtterApiCrudOperation.Put);

        return new OkObjectResult(entity);
    }

    public async Task<ObjectResult> PatchAsync(OtterApiRouteInfo otterApiRouteInfo, JsonObject patch, CancellationToken ct = default)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new OtterApiException(KeylessCode, KeylessError, StatusCodes.Status405MethodNotAllowed);

        if (string.IsNullOrEmpty(otterApiRouteInfo.Id))
            return new BadRequestObjectResult("Id is required in the route for PATCH operations");

        var idValue = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);

        // Single DB round-trip: load the tracked entity through query filters so rows hidden
        // by a tenant / soft-delete filter return 404 rather than being silently modified.
        // Replaces the previous two-query approach (NoTracking snapshot + second tracking load).
        var trackedSet = otterApiRouteInfo.Entity.GetDbSet(dbContext);
        trackedSet = ApplyQueryFilters(trackedSet, otterApiRouteInfo.Entity);
        trackedSet = ApplyScopedQueryFilters(trackedSet, otterApiRouteInfo.Entity);
        trackedSet = otterApiRouteInfo.Entity.WhereId(trackedSet, idValue);
        var tracked = (await otterApiRouteInfo.Entity.ToListAsync(trackedSet, ct)).FirstOrDefault();
        if (tracked == null)
            return new NotFoundObjectResult(null);

        // Build an in-memory snapshot of the scalar property values BEFORE patch mutations.
        // Passed to BeforeSave/AfterSave handlers as the "original" state — navigation
        // properties are left null, consistent with the previous AsNoTracking load behaviour.
        var original = Activator.CreateInstance(otterApiRouteInfo.Entity.EntityType)!;
        foreach (var p in otterApiRouteInfo.Entity.Properties)
            p.SetValue(original, p.GetValue(tracked));

        var patchOptions = registry?.PatchOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

        foreach (var (key, node) in patch)
        {
            var prop = otterApiRouteInfo.Entity.Properties
                .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));

            if (prop == null) continue;

            // Never allow overwriting the primary key via PATCH — changing a PK would cause
            // EF Core to throw or silently corrupt the identity of the row.
            if (prop == otterApiRouteInfo.Entity.Id) continue;

            if (node is null)
            {
                var isNullable = !prop.PropertyType.IsValueType
                                 || Nullable.GetUnderlyingType(prop.PropertyType) != null;
                if (isNullable) prop.SetValue(tracked, null);
            }
            else
            {
                var value = node.Deserialize(prop.PropertyType, patchOptions);
                prop.SetValue(tracked, value);
            }
        }

        if (!IsValid(tracked))
            return new BadRequestObjectResult(actionContext.ModelState);

        foreach (var h in otterApiRouteInfo.Entity.PreSaveHandlers)
            await h(dbContext, tracked, original, OtterApiCrudOperation.Patch);
        await dbContext.SaveChangesAsync(ct);
        foreach (var h in otterApiRouteInfo.Entity.PostSaveHandlers)
            await h(dbContext, tracked, original, OtterApiCrudOperation.Patch);

        return GetOkObjectResult(tracked);
    }

    public async Task<ObjectResult> DeleteAsync(OtterApiRouteInfo otterApiRouteInfo, CancellationToken ct = default)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new OtterApiException(KeylessCode, KeylessError, StatusCodes.Status405MethodNotAllowed);

        if (string.IsNullOrEmpty(otterApiRouteInfo.Id))
            return new BadRequestObjectResult("Id is required in the route for DELETE operations");

        var idValue = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);

        object? entity;

        // Fast path: no query filters → FindAsync (uses EF identity cache, one round-trip)
        if (otterApiRouteInfo.Entity.QueryFilters.Count == 0
            && otterApiRouteInfo.Entity.ScopedQueryFilterFactories.Count == 0)
        {
            entity = await otterApiRouteInfo.Entity.FindByIdAsync(dbContext, idValue, ct);
        }
        else
        {
            // Query filters present → apply them so a record hidden by a filter (e.g. another
            // tenant's row) is treated as non-existent and cannot be deleted.
            // GetDbSet returns a tracking IQueryable, so the loaded entity is tracked and
            // dbContext.Remove() works without an extra round-trip.
            var filteredSet = otterApiRouteInfo.Entity.GetDbSet(dbContext);
            filteredSet = ApplyQueryFilters(filteredSet, otterApiRouteInfo.Entity);
            filteredSet = ApplyScopedQueryFilters(filteredSet, otterApiRouteInfo.Entity);
            filteredSet = otterApiRouteInfo.Entity.WhereId(filteredSet, idValue);
            entity = (await otterApiRouteInfo.Entity.ToListAsync(filteredSet, ct)).FirstOrDefault();
        }

        if (entity == null)
            return new NotFoundObjectResult(null);

        dbContext.Remove(entity);
        foreach (var h in otterApiRouteInfo.Entity.PreSaveHandlers)
            await h(dbContext, entity, entity, OtterApiCrudOperation.Delete);
        await dbContext.SaveChangesAsync(ct);
        foreach (var h in otterApiRouteInfo.Entity.PostSaveHandlers)
            await h(dbContext, entity, entity, OtterApiCrudOperation.Delete);

        return new ObjectResult(null) { StatusCode = StatusCodes.Status204NoContent };
    }

    protected virtual bool IsValid(object entity)
    {
        objectModelValidator.Validate(actionContext, null, "", entity);
        return actionContext.ModelState.IsValid;
    }

    private async Task<object?> LoadOriginalAsync(OtterApiRouteInfo otterApiRouteInfo, CancellationToken ct = default)
    {
        var idValue    = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id!, otterApiRouteInfo.Entity.Id!.PropertyType);
        var dbSet      = otterApiRouteInfo.Entity.GetDbSet(dbContext);
        var noTracking = otterApiRouteInfo.Entity.AsNoTracking(dbSet);
        noTracking = ApplyQueryFilters(noTracking, otterApiRouteInfo.Entity);
        noTracking = ApplyScopedQueryFilters(noTracking, otterApiRouteInfo.Entity);
        noTracking = otterApiRouteInfo.Entity.WhereId(noTracking, idValue);
        return (await otterApiRouteInfo.Entity.ToListAsync(noTracking, ct)).FirstOrDefault();
    }

    /// <summary>
    /// Handles a named custom GET route registered via <c>.WithCustomRoute(...)</c>.
    /// Pipeline:
    /// 1. Entity-level QueryFilters (access control / soft-delete)
    /// 2. Custom route's own Filters (preset scope)
    /// 3. Eagerly loaded navigation properties (from ?include=)
    /// 4. Client-supplied ?filter[...] (further narrowing — AND semantics)
    /// 5. Sort: client ?sort[...] → custom route Sort → default Id desc
    /// 6. Pagination / Take: client skip/take → custom route Take
    /// 7. Single mode: returns first item or 404
    /// </summary>
    private async Task<ObjectResult> GetCustomRouteAsync(OtterApiRouteInfo routeInfo, CancellationToken ct = default)
    {
        var cr    = routeInfo.CustomRoute!;
        var dbSet = routeInfo.Entity.GetDbSet(dbContext);

        dbSet = ApplyQueryFilters(dbSet, routeInfo.Entity);
        dbSet = ApplyScopedQueryFilters(dbSet, routeInfo.Entity);

        // 2. Custom route's own filters
        foreach (var filter in cr.Filters)
            dbSet = filter(dbSet);

        // 3. Navigation property eager loading
        foreach (var include in routeInfo.IncludeExpression)
            dbSet = routeInfo.Entity.Include(dbSet, include);

        if (routeInfo.FilterApply != null)
            dbSet = routeInfo.FilterApply(dbSet);

        // Sort: client sort → custom-route sort → default Id desc
        var sortDelegate = routeInfo.SortApply ?? cr.SortApply;
        dbSet = sortDelegate != null
            ? sortDelegate(dbSet)
            : ApplyDefaultSort(dbSet, routeInfo);

        // 6a. Single mode — return first item (Take(1) for efficiency) or 404
        if (cr.Single)
        {
            var item = (await routeInfo.Entity.ToListAsync(OtterApiDynamicLinq.Take(dbSet, 1), ct)).FirstOrDefault();
            return item != null ? GetOkObjectResult(item) : new NotFoundObjectResult(null);
        }

        // 6b. Apply take/skip: client-specified take overrides custom route take
        var take = ClampPageSize(routeInfo.Take != 0 ? routeInfo.Take : cr.Take);
        if (take > 0)
            dbSet = OtterApiDynamicLinq.Take(OtterApiDynamicLinq.Skip(dbSet, routeInfo.Skip), take);
        else if (routeInfo.Skip > 0)
            dbSet = OtterApiDynamicLinq.Skip(dbSet, routeInfo.Skip);

        return GetOkObjectResult(await routeInfo.Entity.ToListAsync(dbSet, ct));
    }

    private async Task<OtterApiPagedResult> GetPagedResultAsync(OtterApiEntity entity, IQueryable dbSet, int page, int pageSize, CancellationToken ct = default)
    {
        var total    = await entity.CountAsync(dbSet, ct);
        var pagedSet = OtterApiDynamicLinq.Take(OtterApiDynamicLinq.Skip(dbSet, (page - 1) * pageSize), pageSize);

        return new OtterApiPagedResult
        {
            Items     = await entity.ToListAsync(pagedSet, ct),
            Page      = page,
            PageSize  = pageSize,
            PageCount = (int)Math.Ceiling(total / (decimal)pageSize),
            Total     = total
        };
    }

    private static IQueryable ApplyDefaultSort(IQueryable dbSet, OtterApiRouteInfo otterApiRouteInfo)
    {
        if (otterApiRouteInfo.Entity?.Id != null)
            return otterApiRouteInfo.Entity.OrderByIdDesc(dbSet);
        return dbSet;
    }

    /// <summary>
    /// Applies all registered query filters to the given IQueryable.
    /// Each filter is a typed closure that casts to IQueryable&lt;T&gt; and calls .Where(predicate).
    /// Filters are composed in order — all must pass (AND semantics).
    /// No-op when QueryFilters is empty.
    /// </summary>
    private static IQueryable ApplyQueryFilters(IQueryable dbSet, OtterApiEntity entity)
    {
        foreach (var filter in entity.QueryFilters)
            dbSet = filter(dbSet);
        return dbSet;
    }

    /// <summary>
    /// Applies per-request scoped query filters. Each factory receives the request-scoped
    /// IServiceProvider (giving access to IHttpContextAccessor, etc.) and returns a filter closure.
    /// No-op when serviceProvider is null or ScopedQueryFilterFactories is empty.
    /// </summary>
    private IQueryable ApplyScopedQueryFilters(IQueryable dbSet, OtterApiEntity entity)
    {
        if (serviceProvider == null || entity.ScopedQueryFilterFactories.Count == 0)
            return dbSet;
        foreach (var factory in entity.ScopedQueryFilterFactories)
            dbSet = factory(serviceProvider)(dbSet);
        return dbSet;
    }

    /// <summary>
    /// Returns the effective page size to apply on a query.
    /// <list type="bullet">
    ///   <item>When <c>MaxPageSize == 0</c> (no server limit): returns <paramref name="size"/> unchanged.</item>
    ///   <item>When <paramref name="size"/> is 0 (client provided no <c>?pagesize</c>):
    ///         returns <c>MaxPageSize</c> — prevents unbounded queries against large tables.</item>
    ///   <item>When <paramref name="size"/> exceeds <c>MaxPageSize</c>: clamps to <c>MaxPageSize</c>.</item>
    ///   <item>Otherwise: returns <paramref name="size"/> unchanged.</item>
    /// </list>
    /// </summary>
    private int ClampPageSize(int size)
    {
        var max = registry?.Options.MaxPageSize ?? 0;
        if (max <= 0) return size;              // no server-side limit configured
        if (size <= 0 || size > max) return max; // no client limit, or exceeds cap → use MaxPageSize
        return size;
    }

    private OkObjectResult GetOkObjectResult(object result)
    {
        var jsonOptions  = registry?.SerializationOptions ?? FallbackSerializationOptions;
        var objectResult = new OkObjectResult(result);
        objectResult.Formatters.Add(new SystemTextJsonOutputFormatter(jsonOptions));
        return objectResult;
    }
}
