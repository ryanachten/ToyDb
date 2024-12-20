using Google.Protobuf;
using ToyDb.Messages;

namespace ToyDb.Models;

public class DatabaseEntry
{
    public required string Key { get; set; }
    public required DataType Type { get; set; }
    public required ByteString Data { get; set; }

    public static DatabaseEntry Empty(string key) => new () {
        Key = key,
        Type = DataType.Null,
        Data = ByteString.Empty
    };
}
