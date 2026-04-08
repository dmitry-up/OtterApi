using OtterApi.Interfaces;

namespace OtterApi.Expressions;

public class OtterApiSortOtterApiExpression(string propertyName, string sortOrder) : IOtterApiExpression<string>
{
    /// <summary>Returns <c>true</c> when the sort direction is descending.</summary>
    public bool IsDescending =>
        sortOrder.Equals("desc",       StringComparison.OrdinalIgnoreCase) ||
        sortOrder.Equals("descending", StringComparison.OrdinalIgnoreCase) ||
        sortOrder.Equals("1",          StringComparison.Ordinal);

    public string Build()
    {
        return IsDescending ? $"{propertyName} desc" : $"{propertyName} asc";
    }
}