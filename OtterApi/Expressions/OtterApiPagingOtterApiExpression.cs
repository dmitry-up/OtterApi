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

        foreach (var key in queryString.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            if (key.Equals($"{prefix}size", StringComparison.OrdinalIgnoreCase))
            {
                if (uint.TryParse(queryString[key].ToString(), out var parsedSize))
                    pageSize = parsedSize;
            }

            if (key.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // parsedPage == 0 would cause uint underflow in (page - 1U) * pageSize → negative Skip.
                // Treat page=0 the same as page=1 (first page).
                if (uint.TryParse(queryString[key].ToString(), out var parsedPage) && parsedPage >= 1)
                    page = parsedPage;
            }
        }

        result.Take = (int)pageSize;
        result.Skip = (int)((page - 1U) * pageSize);
        result.Page = (int)page;

        return result;
    }
}