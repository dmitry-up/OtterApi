using Microsoft.AspNetCore.Mvc;
using OtterApi.Models;

namespace OtterApi.Interfaces;

public interface IOtterApiRestController
{
    Task<ObjectResult> DeleteAsync(OtterApiRouteInfo otterApiRouteInfo);
    Task<ObjectResult> GetAsync(OtterApiRouteInfo otterApiRouteInfo);
    Task<ObjectResult> PostAsync(OtterApiRouteInfo otterApiRouteInfo, object entity);
    Task<ObjectResult> PutAsync(OtterApiRouteInfo otterApiRouteInfo, object entity);
}