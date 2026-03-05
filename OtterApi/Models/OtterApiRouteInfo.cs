namespace OtterApi.Models;

public class OtterApiRouteInfo
{
    public OtterApiEntity? Entity { get; set; }
    public string? Id { get; set; }
    public string? FilterExpression { get; set; }
    public object[]? FilterValues { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int Page { get; set; }
    public string? SortExpression { get; set; }
    public bool IsCount { get; set; }
    public bool HasModifiers => FilterExpression != null || SortExpression != null || Take != 0 || IncludeExpression.Count > 0;
    public bool IsPageResult { get; set; }
    public List<string> IncludeExpression { get; set; } = [];
}