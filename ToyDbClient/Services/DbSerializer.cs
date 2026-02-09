using Google.Protobuf;
using ToyDbContracts.Routing;

namespace ToyDbClient.Services;

public static class DbSerializer
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

    /// <summary>
    /// Serializes data into key-value pair format accepted by the database
    /// </summary>
    /// <typeparam name="T">Type of the requested data</typeparam>
    /// <param name="key">Key for value retrieval</param>
    /// <param name="value">Value to be encoded into binary format</param>
    /// <returns>Serialized data object</returns>
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

    /// <summary>
    /// Deserializes binary data returned from the ToyDB database
    /// </summary>
    /// <typeparam name="T">Expected data type to be returned</typeparam>
    /// <param name="keyValuePair">Key value pair returned from the database containing binary data</param>
    /// <param name="validateStoredTypes">Will ensure that the stored type aligns with the expected type provided. If set to false, the value will attempt to be converted to expected data type.</param>
    /// <returns>Deserialized data or null</returns>
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
            // Yes these casts are gross, but this is what I came up with
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
        Value = value
    };
}
