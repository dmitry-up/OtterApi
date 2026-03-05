using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Expressions;

public class OtterApiIncludeOtterApiExpression(OtterApiEntity aPiEntity, string values) : IOtterApiExpression<List<string>>
{
    public List<string> Build()
    {
        var items = values.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList();
        items = items.Intersect(aPiEntity.NavigationProperties.Select(x => x.Name), StringComparer.InvariantCultureIgnoreCase).ToList();

        return aPiEntity.NavigationProperties.Where(x => items.Contains(x.Name.ToLower(), StringComparer.InvariantCultureIgnoreCase))
            .Select(x => x.Name).ToList();
    }
}