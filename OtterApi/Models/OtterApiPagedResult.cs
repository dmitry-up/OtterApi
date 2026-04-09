namespace OtterApi.Models;

public class OtterApiPagedResult
{
    public List<object> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageCount { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
}