using System.ComponentModel.DataAnnotations;
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
    private Func<DbContext, object, object?, OtterApiCrudOperation, Task>? postSaveHandler;
    private Func<DbContext, object, object?, OtterApiCrudOperation, Task>? preSaveHandler;
    private string? putPolicy;
    private OtterApiCrudOperation allowedOperations = OtterApiCrudOperation.All;

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

    public OtterApiEntityBuilder<T> BeforeSave(Action<DbContext, T, T?, OtterApiCrudOperation> handler)
    {
        preSaveHandler = (ctx, newEntity, originalEntity, op) =>
        {
            handler(ctx, (T)newEntity, originalEntity is T orig ? orig : default, op);
            return Task.CompletedTask;
        };
        return this;
    }

    public OtterApiEntityBuilder<T> BeforeSave(Func<DbContext, T, T?, OtterApiCrudOperation, Task> handler)
    {
        preSaveHandler = (ctx, newEntity, originalEntity, op) =>
            handler(ctx, (T)newEntity, originalEntity is T orig ? orig : default, op);
        return this;
    }

    public OtterApiEntityBuilder<T> BeforeSave(IOtterApiBeforeSaveHandler<T> handler)
        => BeforeSave(handler.BeforeSaveAsync);

    public OtterApiEntityBuilder<T> AfterSave(Action<DbContext, T, T?, OtterApiCrudOperation> handler)
    {
        postSaveHandler = (ctx, newEntity, originalEntity, op) =>
        {
            handler(ctx, (T)newEntity, originalEntity is T orig ? orig : default, op);
            return Task.CompletedTask;
        };
        return this;
    }

    public OtterApiEntityBuilder<T> AfterSave(Func<DbContext, T, T?, OtterApiCrudOperation, Task> handler)
    {
        postSaveHandler = (ctx, newEntity, originalEntity, op) =>
            handler(ctx, (T)newEntity, originalEntity is T orig ? orig : default, op);
        return this;
    }

    public OtterApiEntityBuilder<T> AfterSave(IOtterApiAfterSaveHandler<T> handler)
        => AfterSave(handler.AfterSaveAsync);

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
            Properties = entityType.GetProperties()
                .Where(x => x.PropertyType.IsTypeSupported()).ToList(),
            NavigationProperties = entityType.GetProperties()
                .Where(x => !x.PropertyType.IsTypeSupported()).ToList(),
            Id = entityType.GetProperties()
                .FirstOrDefault(x => x.IsDefined(typeof(KeyAttribute), false)),
            PreSaveHandler = preSaveHandler,
            PostSaveHandler = postSaveHandler
        };
    }
}