using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using OtterApi.Converters;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Configs;

public class OtterApiRegistry : IOtterApiRegistry
{
    public IReadOnlyList<OtterApiEntity> Entities { get; }
    public OtterApiOptions Options { get; }

    /// <summary>Cached JSON options for serializing responses. Built once at startup.</summary>
    public JsonSerializerOptions SerializationOptions { get; }

    /// <summary>Cached JSON options for deserializing POST/PUT request bodies. Built once at startup.</summary>
    public JsonSerializerOptions DeserializationOptions { get; }

    /// <summary>Cached JSON options for deserializing PATCH (JSON Merge Patch) bodies. Built once at startup.</summary>
    public JsonSerializerOptions PatchOptions { get; }

    /// <summary>
    /// O(1) route index: entity.Route (case-insensitive) → entity.
    /// Built once at startup; replaces the previous O(n) linear scan per request.
    /// </summary>
    private readonly Dictionary<string, OtterApiEntity> _routeIndex;

    public OtterApiRegistry(IReadOnlyList<OtterApiEntity> entities, OtterApiOptions options)
    {
        Entities = entities;
        Options  = options;

        // ── Route index (O(1) lookup) ───────────────────────────────────────────
        // First registration wins when two entities share the same route (preserves
        // previous behaviour of Where().FirstOrDefault()).
        _routeIndex = new Dictionary<string, OtterApiEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
            _routeIndex.TryAdd(entity.Route, entity);

        var baseOptions = options.JsonSerializerOptions;

        // ── Serialization (response output) ────────────────────────────────────
        SerializationOptions = baseOptions == null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            : new JsonSerializerOptions(baseOptions);
        SerializationOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        if (!SerializationOptions.Converters.Any(c => c is OtterApiCaseInsensitiveEnumConverterFactory))
            SerializationOptions.Converters.Add(new OtterApiCaseInsensitiveEnumConverterFactory());

        // ── Deserialization (POST / PUT body) ───────────────────────────────────
        if (baseOptions == null)
        {
            DeserializationOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }
        else
        {
            DeserializationOptions = baseOptions.PropertyNameCaseInsensitive
                ? new JsonSerializerOptions(baseOptions)
                : new JsonSerializerOptions(baseOptions) { PropertyNameCaseInsensitive = true };
        }
        if (!DeserializationOptions.Converters.Any(c => c is OtterApiCaseInsensitiveEnumConverterFactory))
            DeserializationOptions.Converters.Add(new OtterApiCaseInsensitiveEnumConverterFactory());

        // ── Patch deserialization (PATCH body) ──────────────────────────────────
        // Fixed: previously used a fresh JsonSerializerDefaults.Web, which silently
        // ignored any custom converters or naming policies supplied by the caller.
        // Now derived from DeserializationOptions so all user-configured options apply.
        PatchOptions = new JsonSerializerOptions(DeserializationOptions);
    }

    /// <summary>
    /// Finds the registered entity whose route is a segment-boundary prefix of
    /// <paramref name="requestPath"/> and sets <paramref name="remainder"/> to the
    /// path after the matched prefix (e.g. <c>/5</c>, <c>/count</c>, …).
    /// Returns <c>null</c> when no entity matches — the request falls through to the
    /// next middleware.
    /// <para>
    /// Uses at most <b>two O(1) dictionary lookups</b> per request:
    /// <list type="number">
    ///   <item>Exact match → collection endpoint (GET list, POST).</item>
    ///   <item>Parent-segment match → by-ID / sub-route
    ///         (GET by id, PUT, PATCH, DELETE, /count, /pagedresult, custom slugs).</item>
    /// </list>
    /// </para>
    /// </summary>
    public OtterApiEntity? FindEntityForPath(PathString requestPath, out PathString remainder)
    {
        remainder = default;
        var val = requestPath.Value ?? "";

        // Strip trailing slash so "/api/products/" is treated the same as "/api/products".
        var normalized = val.TrimEnd('/');
        if (normalized.Length == 0) normalized = "/";

        // 1. Exact match — collection-level request (no sub-segment after the route).
        if (_routeIndex.TryGetValue(normalized, out var entity))
            return entity; // remainder stays default (HasValue = false)

        // 2. Parent-segment match — strip the last segment and look up the prefix.
        //    Handles: /api/products/5  → route=/api/products  remainder=/5
        //             /api/products/count → route=/api/products  remainder=/count
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash > 0 && _routeIndex.TryGetValue(normalized[..lastSlash], out entity))
        {
            remainder = new PathString(normalized[lastSlash..]);
            return entity;
        }

        return null;
    }
}
