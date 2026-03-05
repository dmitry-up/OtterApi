using Microsoft.EntityFrameworkCore;
using OtterApi.Enums;

namespace OtterApi.Interfaces;

/// <summary>
/// Implement this interface to handle logic before an entity is saved to the database.
/// Register via <c>builder.BeforeSave(handler)</c>.
/// </summary>
public interface IOtterApiBeforeSaveHandler<T> where T : class
{
    Task BeforeSaveAsync(DbContext context, T newEntity, T? originalEntity, OtterApiCrudOperation operation);
}
