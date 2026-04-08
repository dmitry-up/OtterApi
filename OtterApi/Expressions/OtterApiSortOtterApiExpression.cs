using OtterApi.Interfaces;

namespace OtterApi.Expressions;

public class OtterApiSortOtterApiExpression(string propertyName, string sortOrder) : IOtterApiExpression<string>
{
    /// <summary>Returns <c>true</c> when the sort direction is descending.</summary>
    public bool IsDescending => sortOrder.ToLower() switch
    {
        "desc" or "1" or "descending" => true,
        _ => false
    };

    public string Build()
    {
        return IsDescending ? $"{propertyName} desc" : $"{propertyName} asc";
    }
}