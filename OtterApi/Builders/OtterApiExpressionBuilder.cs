using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using OtterApi.Expressions;
using OtterApi.Helpers;
using OtterApi.Models;

namespace OtterApi.Builders;

public class OtterApiExpressionBuilder(IQueryCollection queryString, OtterApiEntity otterApiEntity)
{
    private const string Pagingprefix   = "page";
    private const string Filterprefix   = "filter";
    private const string Sortprefix     = "sort";
    private const string Operatorprefix = "operator";
    private const string Includeprefix  = "include";

    public OtterApiPagingResult BuildPagingResult()
    {
        return new OtterApiPagingOtterApiExpression(queryString, Pagingprefix).Build();
    }

    /// <summary>
    /// Builds a compiled filter delegate from query-string parameters.
    /// Returns <c>null</c> when no valid filter parameters are present.
    /// All predicates are combined into a single typed Expression Tree — no string parsing at request time.
    /// </summary>
    public Func<IQueryable, IQueryable>? BuildFilterResult()
    {
        var useOr = false;
        foreach (var key in queryString.Keys.Where(x => x.ToLower().StartsWith(Operatorprefix)))
        {
            if (((string?)queryString[key])?.ToLower() == "or")
                useOr = true;
        }

        var predicates = new List<LambdaExpression>();

        foreach (var key in queryString.Keys.Where(x => x.ToLower().StartsWith(Filterprefix)))
        {
            var parts = GetQueryStringParts(key);
            if (parts.property == null) continue;

            OtterApiFilterResult filterResult;
            if (parts.queryStringParts.Count == 0)
                filterResult = new OtterApiFilterOtterApiExpression(
                    parts.property, queryString[key]!).Build();
            else if (parts.queryStringParts.Count == 1)
                filterResult = new OtterApiFilterOperatorExpression(
                    parts.property, queryString[key]!, parts.queryStringParts.First()).Build();
            else
                continue;

            if (filterResult.Predicate != null)
                predicates.Add(filterResult.Predicate);
        }

        if (predicates.Count == 0) return null;

        // Combine all predicates under a single shared ParameterExpression
        var sharedParam = predicates[0].Parameters[0];
        Expression combined = predicates[0].Body;

        for (var i = 1; i < predicates.Count; i++)
        {
            var nextBody = new ParameterReplacer(predicates[i].Parameters[0], sharedParam)
                .Visit(predicates[i].Body);
            combined = useOr
                ? Expression.OrElse(combined, nextBody)
                : Expression.AndAlso(combined, nextBody);
        }

        var lambda = Expression.Lambda(combined, sharedParam);
        return q => OtterApiDynamicLinq.Where(q, lambda);
    }

    /// <summary>
    /// Builds a compiled sort delegate from query-string parameters.
    /// Returns <c>null</c> when no sort parameters are present.
    /// The sort lambda is built once per request (at query-parse time) and captured in the closure.
    /// </summary>
    public Func<IQueryable, IQueryable>? BuildSortResult()
    {
        var sorts = new List<(PropertyInfo Property, bool Descending)>();

        foreach (var key in queryString.Keys.Where(x => x.ToLower().StartsWith(Sortprefix)))
        {
            var parts = GetQueryStringParts(key);
            if (parts.property != null)
            {
                var expr = new OtterApiSortOtterApiExpression(parts.property.Name, queryString[key]!);
                sorts.Add((parts.property, expr.IsDescending));
            }
        }

        if (sorts.Count == 0) return null;

        // Build typed OrderBy/ThenBy chain — captured once in the delegate closure
        var steps = new List<(MethodInfo Method, LambdaExpression Lambda)>();
        var entityType = sorts[0].Property.ReflectedType ?? sorts[0].Property.DeclaringType!;
        var isFirst = true;

        foreach (var (propInfo, descending) in sorts)
        {
            var param     = Expression.Parameter(entityType, "x");
            var keyLambda = Expression.Lambda(Expression.Property(param, propInfo), param);

            var baseMethod = isFirst
                ? (descending ? OtterApiDynamicLinq.QueryableOrderByDescending : OtterApiDynamicLinq.QueryableOrderBy)
                : (descending ? OtterApiDynamicLinq.QueryableThenByDescending  : OtterApiDynamicLinq.QueryableThenBy);

            steps.Add((baseMethod.MakeGenericMethod(entityType, propInfo.PropertyType), keyLambda));
            isFirst = false;
        }

        return q =>
        {
            foreach (var (method, lambda) in steps)
                q = (IQueryable)method.Invoke(null, [q, lambda])!;
            return q;
        };
    }

    public List<string> BuildIncludeResult()
    {
        var result = new List<string>();

        foreach (var key in queryString.Keys.Where(x => x.ToLower() == Includeprefix))
        {
            result.AddRange(new OtterApiIncludeOtterApiExpression(otterApiEntity, queryString[key]!).Build());
        }

        return result;
    }

    private (PropertyInfo? property, List<string> queryStringParts) GetQueryStringParts(string key)
    {
        var parts = key.Split(['[', ']'], StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

        return (otterApiEntity.Properties
                    .Where(x => x.Name.ToLower() == parts.FirstOrDefault()?.ToLower())
                    .FirstOrDefault(),
                parts.Skip(1).ToList());
    }

    // ── Private: rewrites one ParameterExpression to another in an expression body ──
    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}