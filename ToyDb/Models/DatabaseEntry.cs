using ToyDb.Messages;

namespace ToyDb.Models;

public class DatabaseEntry
{
    public required DataType Type { get; set; }
    public required byte[] Data { get; set; }

    public static DatabaseEntry Empty() => new () { Type = DataType.Null, Data = [] };
}
