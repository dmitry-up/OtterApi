using System.Linq.Dynamic.Core;
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
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Controllers;

public class OtterApiRestController(
    DbContext dbContext,
    ActionContext actionContext,
    IObjectModelValidator objectModelValidator,
    IServiceProvider? serviceProvider = null,
    OtterApiOptions? options = null)
    : IOtterApiRestController
{
    private const string KeylessError = "Operation not allowed for keyless entities";

    public async Task<ObjectResult> GetAsync(OtterApiRouteInfo otterApiRouteInfo)
    {
        // ── Named custom route (e.g. GET /api/products/featured) ──────────────
        if (otterApiRouteInfo.CustomRoute != null)
            return await GetCustomRouteAsync(otterApiRouteInfo);

        if (otterApiRouteInfo.Id != null)
        {
            if (otterApiRouteInfo.Entity.Id == null)
                throw new Exception(KeylessError);

            // No query filters → fast FindAsync path
            if (otterApiRouteInfo.Entity.QueryFilters.Count == 0
                && otterApiRouteInfo.Entity.ScopedQueryFilterFactories.Count == 0)
            {
                var result = await ((dynamic)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)).FindAsync(
                    OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType));
                return result != null ? GetOkObjectResult(result) : new NotFoundObjectResult(null);
            }

            // Query filters present → use Where so filters are applied in the same SQL query.
            // Returns 404 if the record exists but does not pass the filter (do not reveal its existence).
            var idValue = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);
            var filteredSet = (IQueryable)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)!;
            filteredSet = ApplyQueryFilters(filteredSet, otterApiRouteInfo.Entity);
            filteredSet = ApplyScopedQueryFilters(filteredSet, otterApiRouteInfo.Entity);
            var filteredResult = (await filteredSet
                    .Where($"{otterApiRouteInfo.Entity.Id.Name} == @0", idValue)
                    .ToDynamicListAsync())
                .FirstOrDefault();
            return filteredResult != null ? GetOkObjectResult(filteredResult) : new NotFoundObjectResult(null);
        }

        var dbSet = (IQueryable)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)!;

        dbSet = ApplyQueryFilters(dbSet, otterApiRouteInfo.Entity);
        dbSet = ApplyScopedQueryFilters(dbSet, otterApiRouteInfo.Entity);

        foreach (var include in otterApiRouteInfo.IncludeExpression)
        {
            dbSet = (dynamic)EntityFrameworkQueryableExtensions.Include((dynamic)dbSet, include);
        }

        if (!string.IsNullOrWhiteSpace(otterApiRouteInfo.FilterExpression))
        {
            dbSet = dbSet.Where(otterApiRouteInfo.FilterExpression, otterApiRouteInfo.FilterValues);
        }

        dbSet = !string.IsNullOrWhiteSpace(otterApiRouteInfo.SortExpression)
            ? dbSet.OrderBy(otterApiRouteInfo.SortExpression)
            : ApplyDefaultSort(dbSet, otterApiRouteInfo);

        if (otterApiRouteInfo.IsCount)
        {
            return GetOkObjectResult(await EntityFrameworkQueryableExtensions.CountAsync((dynamic)dbSet));
        }

        if (otterApiRouteInfo.IsPageResult)
        {
            var pageSize = otterApiRouteInfo.Take == 0 ? 10 : otterApiRouteInfo.Take;
            var page = otterApiRouteInfo.Page < 1 ? 1 : otterApiRouteInfo.Page;
            return GetOkObjectResult(await GetPagedResultAsync(dbSet, page, pageSize));
        }

        if (otterApiRouteInfo.Take != 0)
        {
            dbSet = dbSet
                .Skip(otterApiRouteInfo.Skip)
                .Take(otterApiRouteInfo.Take);
        }

        return GetOkObjectResult(await dbSet.ToDynamicListAsync());
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

        if (!objectId.Equals(routeId))
            return new BadRequestObjectResult(null);

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

        // NoTracking snapshot used by handlers to compare before/after
        var original = await LoadOriginalAsync(otterApiRouteInfo);
        if (original == null)
            return new NotFoundObjectResult(null);

        // Tracked entity — EF Core will detect only the modified properties
        object tracked =
            await ((dynamic)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)).FindAsync(
                OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType));

        // Options to handle enums as strings, matching the rest of the library
        var patchOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        patchOptions.Converters.Add(new OtterApiCaseInsensitiveEnumConverterFactory());

        // Apply only the fields present in the patch document
        foreach (var (key, node) in patch)
        {
            var prop = otterApiRouteInfo.Entity.Properties
                .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));

            if (prop == null) continue;   // unknown or navigation property — skip

            if (node is null)
            {
                // RFC 7396: null means "remove" — only applicable to nullable fields
                var isNullable = !prop.PropertyType.IsValueType
                                 || Nullable.GetUnderlyingType(prop.PropertyType) != null;
                if (isNullable)
                    prop.SetValue(tracked, null);
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

        object entity =
            await ((dynamic)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)).FindAsync(
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
        var idValue    = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);
        var dbSet      = (IQueryable)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)!;
        var noTracking = (IQueryable)EntityFrameworkQueryableExtensions.AsNoTracking((dynamic)dbSet);
        noTracking = ApplyQueryFilters(noTracking, otterApiRouteInfo.Entity);
        noTracking = ApplyScopedQueryFilters(noTracking, otterApiRouteInfo.Entity);
        return (await noTracking
                .Where($"{otterApiRouteInfo.Entity.Id.Name} == @0", idValue)
                .ToDynamicListAsync())
            .FirstOrDefault();
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
        var dbSet = (IQueryable)routeInfo.Entity.DbSet.GetValue(dbContext)!;

        dbSet = ApplyQueryFilters(dbSet, routeInfo.Entity);
        dbSet = ApplyScopedQueryFilters(dbSet, routeInfo.Entity);

        // 2. Custom route's own filters
        foreach (var filter in cr.Filters)
            dbSet = filter(dbSet);

        // 3. Navigation property eager loading
        foreach (var include in routeInfo.IncludeExpression)
            dbSet = (dynamic)EntityFrameworkQueryableExtensions.Include((dynamic)dbSet, include);

        // 4. Client-supplied filter
        if (!string.IsNullOrWhiteSpace(routeInfo.FilterExpression))
            dbSet = dbSet.Where(routeInfo.FilterExpression, routeInfo.FilterValues);

        // 5. Sort: client sort takes priority over custom route sort
        var sortExpr = !string.IsNullOrWhiteSpace(routeInfo.SortExpression)
            ? routeInfo.SortExpression
            : cr.Sort;

        dbSet = !string.IsNullOrWhiteSpace(sortExpr)
            ? dbSet.OrderBy(sortExpr)
            : ApplyDefaultSort(dbSet, routeInfo);

        // 6a. Single mode — return first item (Take(1) for efficiency) or 404
        if (cr.Single)
        {
            var item = (await dbSet.Take(1).ToDynamicListAsync()).FirstOrDefault();
            return item != null ? GetOkObjectResult(item) : new NotFoundObjectResult(null);
        }

        // 6b. Apply take/skip: client-specified take overrides custom route take
        var take = routeInfo.Take != 0 ? routeInfo.Take : cr.Take;
        if (take > 0)
            dbSet = dbSet.Skip(routeInfo.Skip).Take(take);
        else if (routeInfo.Skip > 0)
            dbSet = dbSet.Skip(routeInfo.Skip);

        return GetOkObjectResult(await dbSet.ToDynamicListAsync());
    }

    private async Task<OtterApiPagedResult> GetPagedResultAsync(IQueryable dbSet, int page, int pageSize)    {
        var total = await EntityFrameworkQueryableExtensions.CountAsync((dynamic)dbSet);
        var pagedSet = dbSet
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        return new OtterApiPagedResult
        {
            Items = await pagedSet.ToDynamicListAsync(),
            Page = page,
            PageSize = pageSize,
            PageCount = (int)Math.Ceiling(total / (decimal)pageSize),
            Total = total
        };
    }

    private static IQueryable ApplyDefaultSort(IQueryable dbSet, OtterApiRouteInfo otterApiRouteInfo)
    {
        if (otterApiRouteInfo.Entity?.Id != null)
            return dbSet.OrderBy($"{otterApiRouteInfo.Entity.Id.Name} desc");

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
        var baseOptions = options?.JsonSerializerOptions;
        JsonSerializerOptions jsonOptions;

        if (baseOptions == null)
        {
            jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        }
        else
        {
            jsonOptions = new JsonSerializerOptions(baseOptions);
        }

        jsonOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();

        if (!jsonOptions.Converters.Any(c => c is OtterApiCaseInsensitiveEnumConverterFactory))
            jsonOptions.Converters.Add(new OtterApiCaseInsensitiveEnumConverterFactory());

        var objectResult = new OkObjectResult(result);
        objectResult.Formatters.Add(new SystemTextJsonOutputFormatter(jsonOptions));
        return objectResult;
    }
}
