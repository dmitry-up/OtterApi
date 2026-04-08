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
                if (uint.TryParse(queryString[key].ToString(), out var parsedSize))
                    pageSize = parsedSize;
            }

            if (key.ToLower() == prefix)
            {
                if (uint.TryParse(queryString[key].ToString(), out var parsedPage))
                    page = parsedPage;
            }
        }

        result.Take = (int)pageSize;
        result.Skip = (int)((page - 1U) * pageSize);
        result.Page = (int)page;

        return result;
    }
}