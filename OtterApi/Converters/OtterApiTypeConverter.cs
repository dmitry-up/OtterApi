using System.ComponentModel;

namespace OtterApi.Converters;

public class OtterApiTypeConverter
{
    public static object ChangeType(string value, Type type)
    {
        return TypeDescriptor.GetConverter(type).ConvertFromInvariantString(value);
    }
}