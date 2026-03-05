using OtterApi.Configs;
using OtterApi.Models;

namespace OtterApi.Interfaces;

internal interface IOtterApiEntityBuilder
{
    OtterApiEntity Build(Type dbContextType, OtterApiOptions options);
}