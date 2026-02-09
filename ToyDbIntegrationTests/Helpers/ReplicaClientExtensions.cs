using ToyDbRouting.Clients;

namespace ToyDbIntegrationTests.Helpers;

public static class ReplicaClientExtensions
{
    public static async Task<T> GetAndDeserializeValue<T>(this ReplicaClient client, string key)
    {
        var response = await client.GetValue(key);
        return DataSerializer.Deserialize<T>(response);
    }

    public static async Task<T> SetAndDeserializeValue<T>(this ReplicaClient client, string key, T value)
    {
        var keyValuePair = DataSerializer.Serialize(key, value);
        var response = await client.SetValue(keyValuePair);
        return DataSerializer.Deserialize<T>(response);
    }

    public static async Task<Dictionary<string, string>> GetAllValuesAsDictionary(this ReplicaClient client)
    {
        var response = await client.GetAllValues();
        return response.Values.ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value.ToStringUtf8());
    }
}