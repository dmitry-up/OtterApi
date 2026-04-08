using OtterApi.Converters;
using Xunit;

namespace OtterApi.Tests.Converters;

/// <summary>
/// Contract: OtterApiTypeConverter.ChangeType converts a string representation
/// to the requested CLR type using the invariant culture.
/// </summary>
public class TypeConverterTests
{
    // ── Primitive types ───────────────────────────────────────────────────────

    [Fact]
    public void ChangeType_ConvertsString_ToInt()
    {
        var result = OtterApiTypeConverter.ChangeType("42", typeof(int));
        Assert.Equal(42, result);
    }

    [Fact]
    public void ChangeType_ConvertsString_ToLong()
    {
        var result = OtterApiTypeConverter.ChangeType("9876543210", typeof(long));
        Assert.Equal(9876543210L, result);
    }

    [Fact]
    public void ChangeType_ConvertsString_ToDecimal()
    {
        // Invariant culture: period is the decimal separator
        var result = OtterApiTypeConverter.ChangeType("9.99", typeof(decimal));
        Assert.Equal(9.99m, result);
    }

    [Fact]
    public void ChangeType_ConvertsString_ToDouble()
    {
        var result = OtterApiTypeConverter.ChangeType("3.14", typeof(double));
        Assert.Equal(3.14d, (double)result, precision: 10);
    }

    [Fact]
    public void ChangeType_ConvertsString_ToBool_True()
    {
        var result = OtterApiTypeConverter.ChangeType("true", typeof(bool));
        Assert.Equal(true, result);
    }

    [Fact]
    public void ChangeType_ConvertsString_ToBool_False()
    {
        var result = OtterApiTypeConverter.ChangeType("false", typeof(bool));
        Assert.Equal(false, result);
    }

    // ── Guid ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ChangeType_ConvertsString_ToGuid()
    {
        var id     = Guid.NewGuid();
        var result = OtterApiTypeConverter.ChangeType(id.ToString(), typeof(Guid));
        Assert.Equal(id, result);
    }

    // ── DateTime ──────────────────────────────────────────────────────────────

    [Fact]
    public void ChangeType_ConvertsString_ToDateTime()
    {
        var result = OtterApiTypeConverter.ChangeType("2024-01-15", typeof(DateTime));
        Assert.Equal(new DateTime(2024, 1, 15), result);
    }

    // ── String identity ───────────────────────────────────────────────────────

    [Fact]
    public void ChangeType_ConvertsString_ToSameString()
    {
        var result = OtterApiTypeConverter.ChangeType("hello", typeof(string));
        Assert.Equal("hello", result);
    }

    // ── Return type correctness ───────────────────────────────────────────────

    [Fact]
    public void ChangeType_ReturnValue_HasExactType_Int()
    {
        var result = OtterApiTypeConverter.ChangeType("1", typeof(int));
        Assert.IsType<int>(result);
    }

    [Fact]
    public void ChangeType_ReturnValue_HasExactType_Guid()
    {
        var result = OtterApiTypeConverter.ChangeType(Guid.Empty.ToString(), typeof(Guid));
        Assert.IsType<Guid>(result);
    }

    // ── Negative / edge numbers ───────────────────────────────────────────────

    [Fact]
    public void ChangeType_HandlesNegativeInt()
    {
        var result = OtterApiTypeConverter.ChangeType("-100", typeof(int));
        Assert.Equal(-100, result);
    }

    [Fact]
    public void ChangeType_HandlesZero()
    {
        var result = OtterApiTypeConverter.ChangeType("0", typeof(int));
        Assert.Equal(0, result);
    }
}

