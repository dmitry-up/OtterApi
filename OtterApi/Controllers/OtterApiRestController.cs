using System.Linq.Dynamic.Core;
using System.Text.Json;
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
    IObjectModelValidator objectModelValidator)
    : IOtterApiRestController
{
    private const string KeylessError = "Operation not allowed for keyless entities";

    public async Task<ObjectResult> GetAsync(OtterApiRouteInfo otterApiRouteInfo)
    {
        if (otterApiRouteInfo.Id != null)
        {
            if (otterApiRouteInfo.Entity.Id == null)
                throw new Exception(KeylessError);

            var result = await ((dynamic)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)).FindAsync(
                OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType));

            return result != null ? GetOkObjectResult(result) : new NotFoundObjectResult(null);
        }

        var dbSet = (IQueryable)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)!;

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
        {
            return new BadRequestObjectResult(actionContext.ModelState);
        }

        dbContext.Add(entity);
        if (otterApiRouteInfo.Entity.PreSaveHandler != null)
            await otterApiRouteInfo.Entity.PreSaveHandler(dbContext, entity, null, OtterApiCrudOperation.Post);
        await dbContext.SaveChangesAsync();
        if (otterApiRouteInfo.Entity.PostSaveHandler != null)
            await otterApiRouteInfo.Entity.PostSaveHandler(dbContext, entity, null, OtterApiCrudOperation.Post);

        var newId = otterApiRouteInfo.Entity.Id.GetValue(entity);
        return new CreatedResult($"{otterApiRouteInfo.Entity.Route}/{newId}", entity);
    }

    public async Task<ObjectResult> PutAsync(OtterApiRouteInfo otterApiRouteInfo, object entity)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new Exception(KeylessError);

        var objectId = otterApiRouteInfo.Entity.Id.GetValue(entity);
        var routeId = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);

        if (!objectId.Equals(routeId))
        {
            return new BadRequestObjectResult(null);
        }

        if (!IsValid(entity))
        {
            return new BadRequestObjectResult(actionContext.ModelState);
        }

        var original = await LoadOriginalAsync(otterApiRouteInfo);

        if (original == null)
        {
            return new NotFoundObjectResult(null);
        }

        dbContext.Entry(entity).State = EntityState.Modified;
        if (otterApiRouteInfo.Entity.PreSaveHandler != null)
            await otterApiRouteInfo.Entity.PreSaveHandler(dbContext, entity, original, OtterApiCrudOperation.Put);
        await dbContext.SaveChangesAsync();
        if (otterApiRouteInfo.Entity.PostSaveHandler != null)
            await otterApiRouteInfo.Entity.PostSaveHandler(dbContext, entity, original, OtterApiCrudOperation.Put);

        return new OkObjectResult(entity);
    }

    public async Task<ObjectResult> DeleteAsync(OtterApiRouteInfo otterApiRouteInfo)
    {
        if (otterApiRouteInfo.Entity.Id == null)
            throw new Exception(KeylessError);

        object entity =
            await ((dynamic)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)).FindAsync(
                OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType));

        if (entity == null)
        {
            return new NotFoundObjectResult(null);
        }

        dbContext.Remove(entity);
        if (otterApiRouteInfo.Entity.PreSaveHandler != null)
            await otterApiRouteInfo.Entity.PreSaveHandler(dbContext, entity, entity, OtterApiCrudOperation.Delete);
        await dbContext.SaveChangesAsync();
        if (otterApiRouteInfo.Entity.PostSaveHandler != null)
            await otterApiRouteInfo.Entity.PostSaveHandler(dbContext, entity, entity, OtterApiCrudOperation.Delete);

        return new OkObjectResult("");
    }

    protected virtual bool IsValid(object entity)
    {
        objectModelValidator.Validate(actionContext, null, "", entity);
        return actionContext.ModelState.IsValid;
    }

    private async Task<object?> LoadOriginalAsync(OtterApiRouteInfo otterApiRouteInfo)
    {
        var idValue = OtterApiTypeConverter.ChangeType(otterApiRouteInfo.Id, otterApiRouteInfo.Entity.Id.PropertyType);
        var dbSet = (IQueryable)otterApiRouteInfo.Entity.DbSet.GetValue(dbContext)!;
        var noTracking = (IQueryable)EntityFrameworkQueryableExtensions.AsNoTracking((dynamic)dbSet);
        return (await noTracking
                .Where($"{otterApiRouteInfo.Entity.Id.Name} == @0", idValue)
                .ToDynamicListAsync())
            .FirstOrDefault();
    }

    private async Task<OtterApiPagedResult> GetPagedResultAsync(IQueryable dbSet, int page, int pageSize)
    {
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

    private OkObjectResult GetOkObjectResult(object result)
    {
        var baseOptions = OtterApiConfiguration.OtterApiOptions?.JsonSerializerOptions;
        JsonSerializerOptions options;

        if (baseOptions == null)
        {
            options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        }
        else
        {
            options = new JsonSerializerOptions(baseOptions);
        }

        options.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();

        if (!options.Converters.Any(c => c is OtterApiCaseInsensitiveEnumConverterFactory))
            options.Converters.Add(new OtterApiCaseInsensitiveEnumConverterFactory());

        var objectResult = new OkObjectResult(result);
        objectResult.Formatters.Add(new SystemTextJsonOutputFormatter(options));
        return objectResult;
    }
}