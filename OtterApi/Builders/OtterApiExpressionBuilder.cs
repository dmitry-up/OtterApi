using System.Reflection;
using Microsoft.AspNetCore.Http;
using OtterApi.Expressions;
using OtterApi.Models;

namespace OtterApi.Builders;

public class OtterApiExpressionBuilder(IQueryCollection queryString, OtterApiEntity otterApiEntity)
{
    private const string Pagingprefix = "page";
    private const string Filterprefix = "filter";
    private const string Sortprefix = "sort";
    private const string Operatorprefix = "operator";
    private const string Includeprefix = "include";


    public OtterApiPagingResult BuildPagingResult()
    {
        return new OtterApiPagingOtterApiExpression(queryString, Pagingprefix).Build();
    }

    public string? BuildSortResult()
    {
        var expressionList = new List<OtterApiSortOtterApiExpression>();

        foreach (var key in queryString.Keys.Where(x => x.ToLower().StartsWith(Sortprefix)))
        {
            var parts = GetQueryStringParts(key);

            if (parts.property != null)
            {
                expressionList.Add(new OtterApiSortOtterApiExpression(parts.property.Name, queryString[key]));
            }
        }

        if (expressionList.Count == 0)
        {
            return null;
        }

        return string.Join(", ", expressionList.Select(x => x.Build()));
    }

    public OtterApiFilterResult BuildFilterResult()
    {

        var joinOperator = " && ";

        foreach (var key in queryString.Keys.Where(x => x.ToLower().StartsWith(Operatorprefix)))
        {
            switch (((string)queryString[key])?.ToLower())
            {
                case "or":
                    joinOperator = " || ";
                    break;
            }
        }

        var expressionList = new List<OtterApiFilterResult>();
        foreach (var key in queryString.Keys.Where(x => x.ToLower().StartsWith(Filterprefix)))
        {
            var parts = GetQueryStringParts(key);

            if (parts.property != null)
            {
                if (parts.queryStringParts.Count == 0)
                {
                    expressionList.Add(new OtterApiFilterOtterApiExpression(parts.property, queryString[key],
                        expressionList.LastOrDefault()?.NextIndex ?? 0).Build());
                }
                else if (parts.queryStringParts.Count == 1)
                {
                    expressionList.Add(new OtterApiFilterOperatorExpression(parts.property, queryString[key],
                        expressionList.LastOrDefault()?.NextIndex ?? 0, parts.queryStringParts.First()).Build());
                }
            }
        }

        if (expressionList.Count == 0)
        {
            return new OtterApiFilterResult();
        }

        return new OtterApiFilterResult
        {
            Filter = string.Join(joinOperator, expressionList.Select(x => x.Filter)),
            Values = expressionList.SelectMany(x => x.Values).ToArray()
        };
    }

    public List<string> BuildIncludeResult()
    {
        var result = new List<string>();

        foreach (var key in queryString.Keys.Where(x => x.ToLower() == Includeprefix))
        {
            result.AddRange(new OtterApiIncludeOtterApiExpression(otterApiEntity, queryString[key]).Build());
        }

        return result;
    }

    private (PropertyInfo? property, List<string> queryStringParts) GetQueryStringParts(string key)
    {
        var parts = key.Split(['[', ']'], StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

        return (otterApiEntity.Properties.Where(x => x.Name.ToLower() == parts.FirstOrDefault()?.ToLower()).FirstOrDefault(),
            parts.Skip(1).ToList());
    }
}