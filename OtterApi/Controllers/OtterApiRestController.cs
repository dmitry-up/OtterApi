using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using OtterApi.Configs;
using OtterApi.Converters;
using OtterApi.Enums;
using OtterApi.Helpers;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Controllers;

public class OtterApiRestController(
    DbContext dbContext,
    ActionContext actionContext,
    IObjectModelValidator objectModelValidator,
    IServiceProvider? serviceProvider = null,
    OtterApiRegistry? registry = null)
    : IOtterApiRestController
{
    private const string KeylessError = "Operation not allowed for keyless entities";

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

    public async Task<ObjectResult> GetAsync(OtterApiRouteInfo otterApiRouteInfo)
    {
        // ── Named custom route (e.g. GET /api/products/featured) ──────────────
        if (otterApiRouteInfo.CustomRoute != null)
            return await GetCustomRouteAsync(otterApiRouteInfo);

        if (otterApiRouteInfo.Id != null)
        {
            if (otterApiRouteInfo.Entity.Id == null)
                throw new Exception(KeylessError);

            var idValue = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);

            // Fast path: no query filters → FindAsync
            if (otterApiRouteInfo.Entity.QueryFilters.Count == 0
                && otterApiRouteInfo.Entity.ScopedQueryFilterFactories.Count == 0)
            {
                var result = await otterApiRouteInfo.Entity.FindByIdAsync(dbContext, idValue);
                return result != null ? GetOkObjectResult(result) : new NotFoundObjectResult(null);
            }

            // Query filters present → WhereId + filters so the filter is applied in the same SQL query.
            var filteredSet = otterApiRouteInfo.Entity.GetDbSet(dbContext);
            filteredSet = ApplyQueryFilters(filteredSet, otterApiRouteInfo.Entity);
            filteredSet = ApplyScopedQueryFilters(filteredSet, otterApiRouteInfo.Entity);
            filteredSet = otterApiRouteInfo.Entity.WhereId(filteredSet, idValue);
            var filteredResult = (await otterApiRouteInfo.Entity.ToListAsync(filteredSet, CancellationToken.None))
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
            return GetOkObjectResult(await otterApiRouteInfo.Entity.CountAsync(dbSet, CancellationToken.None));

        if (otterApiRouteInfo.IsPageResult)
        {
            var pageSize = otterApiRouteInfo.Take == 0 ? 10 : otterApiRouteInfo.Take;
            var page     = otterApiRouteInfo.Page < 1 ? 1 : otterApiRouteInfo.Page;
            return GetOkObjectResult(await GetPagedResultAsync(otterApiRouteInfo.Entity, dbSet, page, pageSize));
        }

        if (otterApiRouteInfo.Take != 0)
            dbSet = OtterApiDynamicLinq.Take(OtterApiDynamicLinq.Skip(dbSet, otterApiRouteInfo.Skip), otterApiRouteInfo.Take);

        return GetOkObjectResult(await otterApiRouteInfo.Entity.ToListAsync(dbSet, CancellationToken.None));
    }

    public async Task<ObjectResult> PostAsync(OtterApiRouteInfo otterApiRouteInfo, object entity)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new Exception(KeylessError);

        if (!IsValid(entity))
            return new BadRequestObjectResult(actionContext.ModelState);

        dbContext.Add(entity);
        foreach (var h in otterApiRouteInfo.Entity.PreSaveHandlers)
            await h(dbContext, entity, null, OtterApiCrudOperation.Post);
        await dbContext.SaveChangesAsync();
        foreach (var h in otterApiRouteInfo.Entity.PostSaveHandlers)
            await h(dbContext, entity, null, OtterApiCrudOperation.Post);

        var newId = otterApiRouteInfo.Entity.Id.GetValue(entity);
        return new CreatedResult($"{otterApiRouteInfo.Entity.Route}/{newId}", entity);
    }

    public async Task<ObjectResult> PutAsync(OtterApiRouteInfo otterApiRouteInfo, object entity)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new Exception(KeylessError);

        if (string.IsNullOrEmpty(otterApiRouteInfo.Id))
            return new BadRequestObjectResult("Id is required in the route for PUT operations");

        var objectId = otterApiRouteInfo.Entity.Id.GetValue(entity);
        var routeId  = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);

        if (!objectId!.Equals(routeId))
            return new BadRequestObjectResult("Id in route must match Id in the request body");

        if (!IsValid(entity))
            return new BadRequestObjectResult(actionContext.ModelState);

        var original = await LoadOriginalAsync(otterApiRouteInfo);
        if (original == null)
            return new NotFoundObjectResult(null);

        dbContext.Entry(entity).State = EntityState.Modified;
        foreach (var h in otterApiRouteInfo.Entity.PreSaveHandlers)
            await h(dbContext, entity, original, OtterApiCrudOperation.Put);
        await dbContext.SaveChangesAsync();
        foreach (var h in otterApiRouteInfo.Entity.PostSaveHandlers)
            await h(dbContext, entity, original, OtterApiCrudOperation.Put);

        return new OkObjectResult(entity);
    }

    public async Task<ObjectResult> PatchAsync(OtterApiRouteInfo otterApiRouteInfo, JsonObject patch)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new Exception(KeylessError);

        if (string.IsNullOrEmpty(otterApiRouteInfo.Id))
            return new BadRequestObjectResult("Id is required in the route for PATCH operations");

        var original = await LoadOriginalAsync(otterApiRouteInfo);
        if (original == null)
            return new NotFoundObjectResult(null);

        object tracked = (await otterApiRouteInfo.Entity.FindByIdAsync(
            dbContext,
            OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType)))!;

        var patchOptions = registry?.PatchOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

        foreach (var (key, node) in patch)
        {
            var prop = otterApiRouteInfo.Entity.Properties
                .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));

            if (prop == null) continue;

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
        await dbContext.SaveChangesAsync();
        foreach (var h in otterApiRouteInfo.Entity.PostSaveHandlers)
            await h(dbContext, tracked, original, OtterApiCrudOperation.Patch);

        return GetOkObjectResult(tracked);
    }

    public async Task<ObjectResult> DeleteAsync(OtterApiRouteInfo otterApiRouteInfo)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new Exception(KeylessError);

        if (string.IsNullOrEmpty(otterApiRouteInfo.Id))
            return new BadRequestObjectResult("Id is required in the route for DELETE operations");

        var entity = await otterApiRouteInfo.Entity.FindByIdAsync(
            dbContext,
            OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType));

        if (entity == null)
            return new NotFoundObjectResult(null);

        dbContext.Remove(entity);
        foreach (var h in otterApiRouteInfo.Entity.PreSaveHandlers)
            await h(dbContext, entity, entity, OtterApiCrudOperation.Delete);
        await dbContext.SaveChangesAsync();
        foreach (var h in otterApiRouteInfo.Entity.PostSaveHandlers)
            await h(dbContext, entity, entity, OtterApiCrudOperation.Delete);

        return new OkObjectResult("");
    }

    protected virtual bool IsValid(object entity)
    {
        objectModelValidator.Validate(actionContext, null, "", entity);
        return actionContext.ModelState.IsValid;
    }

    private async Task<object?> LoadOriginalAsync(OtterApiRouteInfo otterApiRouteInfo)
    {
        var idValue    = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id!, otterApiRouteInfo.Entity.Id!.PropertyType);
        var dbSet      = otterApiRouteInfo.Entity.GetDbSet(dbContext);
        var noTracking = otterApiRouteInfo.Entity.AsNoTracking(dbSet);
        noTracking = ApplyQueryFilters(noTracking, otterApiRouteInfo.Entity);
        noTracking = ApplyScopedQueryFilters(noTracking, otterApiRouteInfo.Entity);
        noTracking = otterApiRouteInfo.Entity.WhereId(noTracking, idValue);
        return (await otterApiRouteInfo.Entity.ToListAsync(noTracking, CancellationToken.None)).FirstOrDefault();
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
    private async Task<ObjectResult> GetCustomRouteAsync(OtterApiRouteInfo routeInfo)
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
            var item = (await routeInfo.Entity.ToListAsync(OtterApiDynamicLinq.Take(dbSet, 1), CancellationToken.None)).FirstOrDefault();
            return item != null ? GetOkObjectResult(item) : new NotFoundObjectResult(null);
        }

        // 6b. Apply take/skip: client-specified take overrides custom route take
        var take = routeInfo.Take != 0 ? routeInfo.Take : cr.Take;
        if (take > 0)
            dbSet = OtterApiDynamicLinq.Take(OtterApiDynamicLinq.Skip(dbSet, routeInfo.Skip), take);
        else if (routeInfo.Skip > 0)
            dbSet = OtterApiDynamicLinq.Skip(dbSet, routeInfo.Skip);

        return GetOkObjectResult(await routeInfo.Entity.ToListAsync(dbSet, CancellationToken.None));
    }

    private async Task<OtterApiPagedResult> GetPagedResultAsync(OtterApiEntity entity, IQueryable dbSet, int page, int pageSize)
    {
        var total    = await entity.CountAsync(dbSet, CancellationToken.None);
        var pagedSet = OtterApiDynamicLinq.Take(OtterApiDynamicLinq.Skip(dbSet, (page - 1) * pageSize), pageSize);

        return new OtterApiPagedResult
        {
            Items     = await entity.ToListAsync(pagedSet, CancellationToken.None),
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

    private OkObjectResult GetOkObjectResult(object result)
    {
        var jsonOptions  = registry?.SerializationOptions ?? FallbackSerializationOptions;
        var objectResult = new OkObjectResult(result);
        objectResult.Formatters.Add(new SystemTextJsonOutputFormatter(jsonOptions));
        return objectResult;
    }
}
