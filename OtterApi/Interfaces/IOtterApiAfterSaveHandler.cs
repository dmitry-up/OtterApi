using Microsoft.EntityFrameworkCore;
using OtterApi.Enums;

namespace OtterApi.Interfaces;

/// <summary>
/// Implement this interface to handle logic after an entity has been saved to the database.
/// Register via <c>builder.AfterSave(handler)</c>.
/// </summary>
public interface IOtterApiAfterSaveHandler<T> where T : class
{
    Task AfterSaveAsync(DbContext context, T newEntity, T? originalEntity, OtterApiCrudOperation operation);
}
