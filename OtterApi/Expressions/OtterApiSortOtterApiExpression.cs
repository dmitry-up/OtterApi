using OtterApi.Interfaces;

namespace OtterApi.Expressions;

public class OtterApiSortOtterApiExpression(string propertyName, string sortOrder) : IOtterApiExpression<string>
{
    public string Build()
    {
        switch (sortOrder.ToLower())
        {
            case "desc":
            case "1":
            case "descending":
                return $"{propertyName} desc";

            default:
                return $"{propertyName} asc";
        }
    }
}