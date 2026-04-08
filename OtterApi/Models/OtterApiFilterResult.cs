using System.Linq.Expressions;

namespace OtterApi.Models;

public class OtterApiFilterResult
{
    /// <summary>
    /// Typed predicate lambda — <c>Expression&lt;Func&lt;T, bool&gt;&gt;</c> for the entity type.
    /// Null when no matching property was found in the query string.
    /// Multiple predicates are combined via AndAlso / OrElse in
    /// <see cref="OtterApi.Builders.OtterApiExpressionBuilder"/>.
    /// </summary>
    public LambdaExpression? Predicate { get; set; }
}