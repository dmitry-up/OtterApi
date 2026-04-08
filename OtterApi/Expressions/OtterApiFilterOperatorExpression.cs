using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using OtterApi.Configs;
using OtterApi.Converters;
using OtterApi.Exceptions;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Expressions;

public class OtterApiFilterOperatorExpression(PropertyInfo property, string value, string comparisonOperator)
    : IOtterApiExpression<OtterApiFilterResult>
{
    // string.Contains(string) — universally translated by all EF Core providers.
    private static readonly MethodInfo StringContains =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    // string.ToLowerInvariant() — culture-independent lowercase, translated to LOWER(col) by all EF Core providers.
    private static readonly MethodInfo StringToLowerInvariant =
        typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!;

    public OtterApiFilterResult Build()
    {
        if (!property.PropertyType.IsOperatorSupported(comparisonOperator))
            throw new OtterApiException(
                "INVALID_FILTER_OPERATOR",
                $"Operator '{comparisonOperator}' is not supported for type '{property.PropertyType.Name}'.",
                400);

        var entityType = property.ReflectedType ?? property.DeclaringType!;
        var type       = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var param      = Expression.Parameter(entityType, "x");
        var propExpr   = Expression.Property(param, property);

        // ── in / nin: value is a JSON array ──────────────────────────────────
        if (new[] { "in", "nin" }.Contains(comparisonOperator, StringComparer.OrdinalIgnoreCase))
        {
            var listType  = typeof(List<>).MakeGenericType(property.PropertyType);
            var list      = JsonSerializer.Deserialize(value, listType)!;
            var listConst = Expression.Constant(list, listType);
            var containsM = listType.GetMethod("Contains", [property.PropertyType])!;
            var call      = Expression.Call(listConst, containsM, propExpr);
            Expression body = comparisonOperator.Equals("nin", StringComparison.OrdinalIgnoreCase)
                ? Expression.Not(call) : call;
            return new OtterApiFilterResult { Predicate = Expression.Lambda(body, param) };
        }

        // ── Scalar operators ──────────────────────────────────────────────────
        var converted = OtterApiTypeConverter.ChangeType(value, type);
        var constExpr = OtterApiFilterOtterApiExpression.BuildConstant(converted, property.PropertyType);
        var opName    = comparisonOperator.ToLowerInvariant();

        Expression pred = opName switch
        {
            "eq"    => Expression.Equal(propExpr, constExpr),
            "neq"   => Expression.NotEqual(propExpr, constExpr),
            "lt"    => Expression.LessThan(propExpr, constExpr),
            "lteq"  => Expression.LessThanOrEqual(propExpr, constExpr),
            "gt"    => Expression.GreaterThan(propExpr, constExpr),
            "gteq"  => Expression.GreaterThanOrEqual(propExpr, constExpr),

            // LOWER(col) LIKE '%value%' — case-insensitive, translatable by all EF Core providers.
            "like"  => Expression.Call(
                           Expression.Call(propExpr, StringToLowerInvariant),
                           StringContains,
                           Expression.Constant(((string)converted).ToLowerInvariant())),
            "nlike" => Expression.Not(Expression.Call(
                           Expression.Call(propExpr, StringToLowerInvariant),
                           StringContains,
                           Expression.Constant(((string)converted).ToLowerInvariant()))),

            _ => throw new OtterApiException(
                     "INVALID_FILTER_OPERATOR",
                     $"Operator '{comparisonOperator}' is not supported for type '{property.PropertyType.Name}'.",
                     400)
        };

        return new OtterApiFilterResult { Predicate = Expression.Lambda(pred, param) };
    }
}