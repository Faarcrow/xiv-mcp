using System.Text.Json.Nodes;
using XivMcp.Mcp;

namespace XivMcp.Tests;

public class JsonNumberTests
{
    private static JsonNode? Value(string json) => JsonNode.Parse($$"""{ "v": {{json}} }""")!["v"];

    [Fact]
    public void Whole_numbers_are_read_as_they_are()
    {
        Assert.Equal(12, JsonNumber.Int(Value("12")));
        Assert.Equal(-5, JsonNumber.Int(Value("-5")));
    }

    [Fact]
    public void Fractional_numbers_do_not_throw()
    {
        // Regression: Teamcraft's timed-node data has durations like 1.5 (hours); GetValue<int> threw
        // "An element of type 'Number' cannot be converted to a 'System.Int32'" and broke plan_craft for big projects.
        Assert.Throws<InvalidOperationException>(() => Value("1.5")!.GetValue<int>());
        Assert.Equal(2, JsonNumber.Int(Value("1.5")));
        Assert.Equal(12, JsonNumber.Int(Value("12.0")));
        Assert.Equal(3, JsonNumber.Int(Value("3.4")));
    }

    [Fact]
    public void Numbers_put_in_from_code_are_read_whatever_their_type()
    {
        // Regression: a value set from code keeps its own type and reads back only as that type. A job step's memory holds recipe
        // ids as uint, so a retry in the same session lost them and crafted everything again; only after a reload (memory read
        // back from jobs.json as text) did it continue.
        var o = new JsonObject { ["uint"] = 1008u, ["ushort"] = (ushort)7, ["byte"] = (byte)3, ["ulong"] = 5ul, ["float"] = 2.5f, ["decimal"] = 4.4m };
        Assert.Equal(1008, JsonNumber.Int(o["uint"]));
        Assert.Equal(7, JsonNumber.Int(o["ushort"]));
        Assert.Equal(3, JsonNumber.Int(o["byte"]));
        Assert.Equal(5, JsonNumber.Int(o["ulong"]));
        Assert.Equal(3, JsonNumber.Int(o["float"]));
        Assert.Equal(4, JsonNumber.Int(o["decimal"]));
        Assert.Equal(int.MaxValue, JsonNumber.Int(new JsonObject { ["v"] = uint.MaxValue }["v"]));
        Assert.Null(JsonNumber.Int(new JsonObject { ["v"] = true }["v"]));
    }

    [Theory]
    [InlineData(double.NaN, null)]
    [InlineData(double.PositiveInfinity, int.MaxValue)]
    [InlineData(double.NegativeInfinity, int.MinValue)]
    public void Not_a_number_is_null_and_infinities_are_clamped_whatever_their_type(double value, int? expected)
    {
        // A float or Half NaN or infinity calls itself a Number but has no JSON text, so it is read as its own type, not from its text.
        var o = new JsonObject { ["double"] = value, ["float"] = (float)value, ["half"] = JsonValue.Create((Half)value) };
        Assert.Equal(expected, JsonNumber.Int(o["double"]));
        Assert.Equal(expected, JsonNumber.Int(o["float"]));
        Assert.Equal(expected, JsonNumber.Int(o["half"]));
    }

    [Fact]
    public void Numbers_outside_the_int_range_are_clamped()
    {
        Assert.Equal(int.MaxValue, JsonNumber.Int(Value("3000000000")));
        Assert.Equal(int.MinValue, JsonNumber.Int(Value("-3000000000")));
        Assert.Equal(int.MaxValue, JsonNumber.Int(Value("1e30")));
    }

    [Fact]
    public void Numeric_strings_are_read_and_anything_else_is_null()
    {
        Assert.Equal(7, JsonNumber.Int(Value("\"7\"")));
        Assert.Null(JsonNumber.Int(Value("\"seven\"")));
        Assert.Null(JsonNumber.Int(Value("true")));
        Assert.Null(JsonNumber.Int(Value("{}")));
        Assert.Null(JsonNumber.Int(null));
    }

    [Fact]
    public void Created_values_work_too()
    {
        Assert.Equal(4, JsonNumber.Int(JsonValue.Create(4.0)));
        Assert.Equal(9, JsonNumber.Int(JsonValue.Create(9L)));
    }
}
