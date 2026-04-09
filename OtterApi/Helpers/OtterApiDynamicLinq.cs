using System.Linq.Expressions;
using System.Reflection;

namespace OtterApi.Helpers;

/// <summary>
/// Low-level helpers for applying typed Expression Trees and pagination
/// to an untyped <see cref="IQueryable"/>.
/// All Queryable method definitions are cached statically — no per-request reflection overhead.
/// </summary>
internal static class OtterApiDynamicLinq
{
    // ── Cached open-generic Queryable method definitions (resolved once) ──────

    private static readonly MethodInfo QueryableWhere =
        typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.Where) && m.GetParameters().Length == 2);

    internal static readonly MethodInfo QueryableOrderBy =
        typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.OrderBy) && m.GetParameters().Length == 2);

    internal static readonly MethodInfo QueryableOrderByDescending =
        typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.OrderByDescending) && m.GetParameters().Length == 2);

    internal static readonly MethodInfo QueryableThenBy =
        typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.ThenBy) && m.GetParameters().Length == 2);

    internal static readonly MethodInfo QueryableThenByDescending =
        typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.ThenByDescending) && m.GetParameters().Length == 2);

    private static readonly MethodInfo QueryableSkip =
        typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.Skip) && m.GetParameters().Length == 2);

    private static readonly MethodInfo QueryableTake =
        typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.Take)
                        && m.GetParameters().Length == 2
                        && m.GetParameters()[1].ParameterType == typeof(int));

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a pre-built predicate lambda to the untyped IQueryable.
    /// Used by <see cref="OtterApi.Builders.OtterApiExpressionBuilder"/> after combining predicates.
    /// </summary>
    public static IQueryable Where(IQueryable source, LambdaExpression predicate) =>
        (IQueryable)QueryableWhere
            .MakeGenericMethod(source.ElementType)
            .Invoke(null, [source, predicate])!;

    /// <summary>
    /// Applies ORDER BY from a sort expression like <c>"Name asc, Price desc"</c>.
    /// Used for custom-route sorts configured via <c>.WithCustomRoute(sort: "...")</c>.
    /// </summary>
    public static IQueryable OrderBy(IQueryable source, string sortExpression)
    {
        var elementType = source.ElementType;
        var isFirst     = true;

        foreach (var segment in sortExpression.Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens     = segment.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var propName   = tokens[0];
            var descending = tokens.Length > 1 &&
                             tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            var propInfo  = elementType.GetProperty(propName,
                                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                            ?? throw new InvalidOperationException(
                                $"OtterApi: Property '{propName}' not found on '{elementType.Name}'.");
            var param     = Expression.Parameter(elementType, "x");
            var keyLambda = Expression.Lambda(Expression.Property(param, propInfo), param);

            var baseMethod = isFirst
                ? (descending ? QueryableOrderByDescending : QueryableOrderBy)
                : (descending ? QueryableThenByDescending  : QueryableThenBy);

            source = (IQueryable)baseMethod
                .MakeGenericMethod(elementType, propInfo.PropertyType)
                .Invoke(null, [source, keyLambda])!;

            isFirst = false;
        }

        return source;
    }

    /// <summary>Skips <paramref name="count"/> elements on an untyped IQueryable.</summary>
    public static IQueryable Skip(IQueryable source, int count) =>
        (IQueryable)QueryableSkip
            .MakeGenericMethod(source.ElementType)
            .Invoke(null, [source, count])!;

    /// <summary>Takes <paramref name="count"/> elements from an untyped IQueryable.</summary>
    public static IQueryable Take(IQueryable source, int count) =>
        (IQueryable)QueryableTake
            .MakeGenericMethod(source.ElementType)
            .Invoke(null, [source, count])!;
}
