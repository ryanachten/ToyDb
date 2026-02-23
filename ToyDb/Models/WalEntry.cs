using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using ToyDbContracts.Data;

namespace ToyDb.Models;

public class WalEntry
{
    public required long Lsn { get; set; }
    public required Timestamp Timestamp { get; set; }
    public required string Key { get; set; }
    public required DataType Type { get; set; }
    public required ByteString? Data { get; set; }
    public required bool IsDelete { get; set; }
}
