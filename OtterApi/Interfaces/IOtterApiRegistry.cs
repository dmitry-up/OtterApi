using System.Text.Json;
using Microsoft.AspNetCore.Http;
using OtterApi.Configs;
using OtterApi.Models;

namespace OtterApi.Interfaces;

/// <summary>
/// Read-only view of the startup-time OtterApi configuration that is
/// registered as a singleton in the DI container.
/// Consume this interface instead of the concrete <see cref="OtterApiRegistry"/>
/// so that consumers remain testable and the implementation is swappable.
/// </summary>
public interface IOtterApiRegistry
{
    /// <summary>All registered entities.</summary>
    IReadOnlyList<OtterApiEntity> Entities { get; }

    /// <summary>Global options supplied at startup.</summary>
    OtterApiOptions Options { get; }

    /// <summary>Cached JSON options used when serializing response bodies.</summary>
    JsonSerializerOptions SerializationOptions { get; }

    /// <summary>Cached JSON options used when deserializing POST / PUT request bodies.</summary>
    JsonSerializerOptions DeserializationOptions { get; }

    /// <summary>Cached JSON options used when deserializing PATCH document bodies.</summary>
    JsonSerializerOptions PatchOptions { get; }

    /// <summary>
    /// O(1) route lookup. Finds the entity whose registered route is a
    /// segment-boundary prefix of <paramref name="requestPath"/> and sets
    /// <paramref name="remainder"/> to the path suffix (e.g. <c>/5</c>).
    /// Returns <c>null</c> when no entity matches.
    /// </summary>
    OtterApiEntity? FindEntityForPath(PathString requestPath, out PathString remainder);
}

