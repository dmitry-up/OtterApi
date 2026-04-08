using System.Reflection;
using System.Text.Json;
using OtterApi.Configs;
using OtterApi.Converters;
using OtterApi.Models;

namespace OtterApi.Expressions;

public class OtterApiFilterOperatorExpression(PropertyInfo property, string value, int index, string comparisonOperator)
{
    public OtterApiFilterResult Build()
    {
        if (!property.PropertyType.IsOperatorSuported(comparisonOperator))
        {
            throw new NotSupportedException($"Operator {comparisonOperator} is not suported for {property.PropertyType.Name}");
        }

        object list = null;

        if (new[] { "in", "nin" }.Contains(comparisonOperator, StringComparer.OrdinalIgnoreCase))
        {
            var listType = typeof(List<>).MakeGenericType(property.PropertyType);

            list = JsonSerializer.Deserialize(value, listType);
        }

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        return new OtterApiFilterResult
        {
            Filter = OtterApiConfiguration.Operators
                .Where(x => x.Name.Equals(comparisonOperator, StringComparison.InvariantCultureIgnoreCase)).First().Expression
                .Replace("{propertyName}", property.Name).Replace("{index}", index.ToString()),
            Values = [list ?? OtterApiTypeConverter.ChangeType(value, type)],
            NextIndex = index + 1
        };
    }
}