using ToyDbClient.Services;
using ToyDbContracts.Routing;

namespace ToyDbUnitTests.Client.Services.Serializer;

public class GivenUnserializedData
{
    [Theory]
    [InlineData("world", DataType.String)]
    [InlineData(Int32.MinValue, DataType.Int)]
    [InlineData(Int64.MaxValue, DataType.Long)]
    [InlineData(double.MaxValue, DataType.Double)]
    [InlineData(true, DataType.Bool)]
    public void WhenSerializingData_ThenDataShouldBeSerializedBasedonDataType<T>(T value, DataType expectedDataType)
    {
        var key = "hello";

        var result = DbSerializer.Serialize(key, value);
        var parsedResult = DbSerializer.Deserialize<T>(new KeyValueResponse()
        {
            Key = key,
            Type = result.Type,
            Value = result.Value
        });

        Assert.Equal(expectedDataType, result.Type);
        Assert.Equal(value, parsedResult);
    }
}
