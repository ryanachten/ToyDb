using Google.Protobuf;
using ToyDbContracts.Data;

namespace ToyDbIntegrationTests.Helpers;

public static class DataSerializer
{
    private static readonly Dictionary<Type, DataType> _typeBindings = new()
    {
        {typeof(bool), DataType.Bool},
        {typeof(int), DataType.Int},
        {typeof(long), DataType.Long},
        {typeof(float), DataType.Float},
        {typeof(double), DataType.Double},
        {typeof(string), DataType.String},
    };

    public static KeyValueRequest Serialize<T>(string key, T value)
    {
        if (EqualityComparer<T>.Default.Equals(value, default))
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (typeof(T) == typeof(string))
            return CreateRequest(key, DataType.String, ByteString.CopyFromUtf8((string)(object)value!));

        if (typeof(T) == typeof(bool))
            return CreateRequest(key, DataType.Bool, BitConverter.GetBytes((bool)(object)value!));

        if (typeof(T) == typeof(int))
            return CreateRequest(key, DataType.Int, BitConverter.GetBytes((int)(object)value!));

        if (typeof(T) == typeof(long))
            return CreateRequest(key, DataType.Long, BitConverter.GetBytes((long)(object)value!));

        if (typeof(T) == typeof(float))
            return CreateRequest(key, DataType.Float, BitConverter.GetBytes((float)(object)value!));

        if (typeof(T) == typeof(double))
            return CreateRequest(key, DataType.Double, BitConverter.GetBytes((double)(object)value!));

        throw new NotSupportedException($"Serialization of value with provided type {typeof(T)} is not supported.");
    }

    public static T Deserialize<T>(KeyValueResponse keyValuePair, bool validateStoredTypes = true)
    {
        var storedType = keyValuePair.Type;
        var value = keyValuePair.Value;

        if (value == null || storedType == DataType.Null)
            return (T)(object)null!;

        var hasBinding = _typeBindings.TryGetValue(typeof(T), out var requestedType);
        if (!hasBinding)
            throw new NotSupportedException($"Deserialization of value with provided type {typeof(T)} is not supported. Value found with stored type {storedType}.");

        if (validateStoredTypes && requestedType != storedType)
            throw new InvalidOperationException($"Requested type {requestedType} does not match stored type {storedType}");

        var bytes = value.ToByteArray();

        return requestedType switch
        {
            DataType.String => (T)(object)value.ToStringUtf8(),
            DataType.Bool => (T)(object)BitConverter.ToBoolean(bytes),
            DataType.Int => (T)(object)BitConverter.ToInt32(bytes),
            DataType.Long => (T)(object)BitConverter.ToInt64(bytes),
            DataType.Float => (T)(object)BitConverter.ToSingle(bytes),
            DataType.Double => (T)(object)BitConverter.ToDouble(bytes),
            _ => (T)(object)null!
        };
    }

    private static KeyValueRequest CreateRequest(string key, DataType type, byte[] value)
        => CreateRequest(key, type, ByteString.CopyFrom(value));

    private static KeyValueRequest CreateRequest(string key, DataType type, ByteString value) => new()
    {
        Key = key,
        Type = type,
        Value = value,
        Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
    };
}
