using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using OtterApi.Models;

namespace OtterApi.Interfaces;

public interface IOtterApiRestController
{
    Task<ObjectResult> DeleteAsync(OtterApiRouteInfo otterApiRouteInfo, CancellationToken ct = default);
    Task<ObjectResult> GetAsync(OtterApiRouteInfo otterApiRouteInfo, CancellationToken ct = default);
    Task<ObjectResult> PatchAsync(OtterApiRouteInfo otterApiRouteInfo, JsonObject patch, CancellationToken ct = default);
    Task<ObjectResult> PostAsync(OtterApiRouteInfo otterApiRouteInfo, object entity, CancellationToken ct = default);
    Task<ObjectResult> PutAsync(OtterApiRouteInfo otterApiRouteInfo, object entity, CancellationToken ct = default);
}