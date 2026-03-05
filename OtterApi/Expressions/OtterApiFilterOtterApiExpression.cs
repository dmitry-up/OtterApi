using System.Reflection;
using OtterApi.Converters;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Expressions;

public class OtterApiFilterOtterApiExpression(PropertyInfo property, string value, int index) : IOtterApiExpression<OtterApiFilterResult>
{
    public OtterApiFilterResult Build()
    {
        return new OtterApiFilterResult
        {
            Filter = $"{property.Name} == @{index}",
            Values = [OtterApiTypeConverter.ChangeType(value, property.PropertyType)],
            NextIndex = index + 1
        };
    }
}