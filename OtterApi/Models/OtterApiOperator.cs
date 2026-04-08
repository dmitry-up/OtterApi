namespace OtterApi.Models;

public class OtterApiOperator
{
    public string Name { get; set; }
    public bool SupportsString { get; set; }
    public bool SupportsValueType { get; set; }
    public bool SupportsGuid { get; set; }
}