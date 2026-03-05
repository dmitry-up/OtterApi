namespace OtterApi.Models;

public class OtterApiFilterResult
{
    public string Filter { get; set; }

    public object[] Values { get; set; }

    public int NextIndex { get; set; }
}