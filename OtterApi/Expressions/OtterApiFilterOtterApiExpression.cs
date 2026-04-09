using System.Linq.Expressions;
using System.Reflection;
using OtterApi.Converters;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Expressions;

public class OtterApiFilterOtterApiExpression(PropertyInfo property, string value)
    : IOtterApiExpression<OtterApiFilterResult>
{
    public OtterApiFilterResult Build()
    {
        var entityType = property.ReflectedType ?? property.DeclaringType!;
        var param      = Expression.Parameter(entityType, "x");
        var propExpr   = Expression.Property(param, property);
        var converted  = OtterApiTypeConverter.ChangeType(value, property.PropertyType);
        var constExpr  = BuildConstant(converted, property.PropertyType);
        var body       = Expression.Equal(propExpr, constExpr);

        return new OtterApiFilterResult { Predicate = Expression.Lambda(body, param) };
    }

    internal static Expression BuildConstant(object converted, Type propType)
    {
        var underlying = Nullable.GetUnderlyingType(propType);
        if (underlying != null)
            return Expression.Convert(Expression.Constant(converted, underlying), propType);
        return Expression.Constant(converted, propType);
    }
}