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
    ///
    /// Supports two syntaxes (both can be mixed in one request):
    ///
    /// Flat (legacy, backward-compatible):
    ///   filter[Name]=Alice &amp; filter[Price][gt]=10 &amp; operator=or
    ///   → all flat filters go into the implicit "default" group.
    ///
    /// Grouped (new):
    ///   filter[0][Name]=Alice &amp; filter[0][Price][gt]=10 &amp; operator[0]=or &amp; filter[1][CategoryId]=1
    ///   → (Name == "Alice" OR Price > 10) AND CategoryId == 1
    ///
    /// Rules:
    ///   • Filters within the same group are combined with that group's operator (AND by default, OR if operator[N]=or).
    ///   • Groups are always combined with AND.
    ///   • The group index N must be a non-negative integer to distinguish it from a property name.
    /// </summary>
    public Func<IQueryable, IQueryable>? BuildFilterResult()
    {
        // ── Collect per-group operators ───────────────────────────────────────
        // key "operator"    → "default" group uses OR
        // key "operator[N]" → group N uses OR
        var groupOperators = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in queryString.Keys)
        {
            if (!key.Equals(Operatorprefix, StringComparison.OrdinalIgnoreCase) &&
                !key.StartsWith(Operatorprefix + "[", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!((string?)queryString[key])?.Equals("or", StringComparison.OrdinalIgnoreCase) ?? true)
                continue;

            var groupId = ParseOperatorGroupId(key) ?? "default";
            groupOperators[groupId] = true;
        }

        // ── Collect per-group predicates ──────────────────────────────────────
        var groups = new Dictionary<string, List<LambdaExpression>>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in queryString.Keys.Where(x =>
                     x.StartsWith(Filterprefix, StringComparison.OrdinalIgnoreCase)))
        {
            var (groupId, property, opParts) = ParseFilterKey(key);
            if (property == null) continue;

            OtterApiFilterResult filterResult;
            if (opParts.Count == 0)
                filterResult = new OtterApiFilterOtterApiExpression(property, queryString[key]!).Build();
            else if (opParts.Count == 1)
                filterResult = new OtterApiFilterOperatorExpression(property, queryString[key]!, opParts[0]).Build();
            else
                continue;

            if (filterResult.Predicate == null) continue;

            if (!groups.TryGetValue(groupId, out var list))
                groups[groupId] = list = [];
            list.Add(filterResult.Predicate);
        }

        if (groups.Count == 0) return null;

        // ── Combine: OR within each group, then AND between groups ───────────
        var groupPredicates = new List<LambdaExpression>();
        foreach (var (groupId, predicates) in groups)
        {
            var useOr   = groupOperators.GetValueOrDefault(groupId, false);
            var combined = CombinePredicates(predicates, useOr);
            if (combined != null) groupPredicates.Add(combined);
        }

        if (groupPredicates.Count == 0) return null;

        var final = CombinePredicates(groupPredicates, useOr: false)!;
        return q => OtterApiDynamicLinq.Where(q, final);
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

    /// <summary>
    /// Parses a filter key into (groupId, property, operatorParts).
    /// filter[Name]          → ("default", prop, [])
    /// filter[Name][gt]      → ("default", prop, ["gt"])
    /// filter[0][Name]       → ("0",       prop, [])
    /// filter[0][Name][gt]   → ("0",       prop, ["gt"])
    /// The group prefix is recognised only when the first segment is a non-negative integer.
    /// </summary>
    private (string groupId, PropertyInfo? property, List<string> opParts) ParseFilterKey(string key)
    {
        var segments = key.Split(['[', ']'], StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

        string       groupId;
        List<string> tail;

        if (segments.Count > 0 && int.TryParse(segments[0], out var n) && n >= 0)
        {
            groupId = segments[0];
            tail    = segments.Skip(1).ToList();
        }
        else
        {
            groupId = "default";
            tail    = segments;
        }

        var property = otterApiEntity.Properties
            .FirstOrDefault(x => x.Name.Equals(tail.FirstOrDefault(), StringComparison.OrdinalIgnoreCase));

        return (groupId, property, tail.Skip(1).ToList());
    }

    /// <summary>
    /// Extracts the group ID from an operator key.
    /// "operator"    → null  (caller maps to "default")
    /// "operator[0]" → "0"
    /// </summary>
    private static string? ParseOperatorGroupId(string key)
    {
        var segments = key.Split(['[', ']'], StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        return segments.Count > 0 ? segments[0] : null;
    }

    /// <summary>Kept for BuildSortResult which uses the flat sort-key format.</summary>
    private (PropertyInfo? property, List<string> queryStringParts) GetQueryStringParts(string key)
    {
        var parts = key.Split(['[', ']'], StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

        return (otterApiEntity.Properties
                    .Where(x => x.Name.ToLower() == parts.FirstOrDefault()?.ToLower())
                    .FirstOrDefault(),
                parts.Skip(1).ToList());
    }

    /// <summary>
    /// Combines predicates with AndAlso or OrElse, rewriting all ParameterExpressions
    /// to a single shared instance via <see cref="ParameterReplacer"/>.
    /// </summary>
    private static LambdaExpression? CombinePredicates(List<LambdaExpression> predicates, bool useOr)
    {
        if (predicates.Count == 0) return null;
        if (predicates.Count == 1) return predicates[0];

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

        return Expression.Lambda(combined, sharedParam);
    }

    // ── Private: rewrites one ParameterExpression to another in an expression body ──
    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}