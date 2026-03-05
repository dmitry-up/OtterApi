using Microsoft.AspNetCore.Http;
using OtterApi.Interfaces;
using OtterApi.Models;

namespace OtterApi.Expressions;

public class OtterApiPagingOtterApiExpression(IQueryCollection queryString, string prefix) : IOtterApiExpression<OtterApiPagingResult>
{
    public OtterApiPagingResult Build()
    {
        var result = new OtterApiPagingResult();
        var pageSize = 0U;
        var page = 1U;

        foreach (var key in queryString.Keys.Where(x => x.ToLower().StartsWith(prefix)))
        {
            if (key.ToLower() == $"{prefix}size")
            {
                uint.TryParse(queryString[key].ToString(), out pageSize);
            }

            if (key.ToLower() == prefix)
            {
                uint.TryParse(queryString[key].ToString(), out page);
            }
        }

        result.Take = (int)pageSize;
        result.Skip = (int)((page - 1U) * pageSize);
        result.Page = (int)page;

        return result;
    }
}