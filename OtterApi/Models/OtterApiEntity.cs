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

    /// <summary>newEntity = incoming data, originalEntity = current DB state (null for POST)</summary>
    public Func<DbContext, object, object?, OtterApiCrudOperation, Task>? PreSaveHandler { get; set; }

    /// <summary>newEntity = saved data, originalEntity = DB state before save (null for POST)</summary>
    public Func<DbContext, object, object?, OtterApiCrudOperation, Task>? PostSaveHandler { get; set; }
}