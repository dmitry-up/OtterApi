namespace OtterApi.Models;

/// <summary>
/// Describes a filter operator supported by OtterApi.
/// Instances are immutable — all properties are init-only.
/// </summary>
public class OtterApiOperator
{
    public string Name { get; init; } = string.Empty;
    public bool SupportsString { get; init; }
    public bool SupportsValueType { get; init; }
    public bool SupportsGuid { get; init; }
}