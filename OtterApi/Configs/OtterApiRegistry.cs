using OtterApi.Models;

namespace OtterApi.Configs;

public class OtterApiRegistry
{
    public IReadOnlyList<OtterApiEntity> Entities { get; }
    public OtterApiOptions Options { get; }

    public OtterApiRegistry(IReadOnlyList<OtterApiEntity> entities, OtterApiOptions options)
    {
        Entities = entities;
        Options  = options;
    }
}

