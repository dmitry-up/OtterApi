namespace OtterApi.Enums;

[Flags]
public enum OtterApiCrudOperation
{
    Get    = 1,
    Post   = 2,
    Put    = 4,
    Delete = 8,
    All    = Get | Post | Put | Delete
}