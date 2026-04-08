using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OtterApi.Converters;
using OtterApi.Models;

namespace OtterApi.Configs;

public class OtterApiRegistry
{
    public IReadOnlyList<OtterApiEntity> Entities { get; }
    public OtterApiOptions Options { get; }

    /// <summary>Cached JSON options for serializing responses. Built once at startup.</summary>
    public JsonSerializerOptions SerializationOptions { get; }

    /// <summary>Cached JSON options for deserializing POST/PUT request bodies. Built once at startup.</summary>
    public JsonSerializerOptions DeserializationOptions { get; }

    /// <summary>Cached JSON options for deserializing PATCH (JSON Merge Patch) bodies. Built once at startup.</summary>
    public JsonSerializerOptions PatchOptions { get; }

    public OtterApiRegistry(IReadOnlyList<OtterApiEntity> entities, OtterApiOptions options)
    {
        Entities = entities;
        Options  = options;

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
        PatchOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        PatchOptions.Converters.Add(new OtterApiCaseInsensitiveEnumConverterFactory());
    }
}
