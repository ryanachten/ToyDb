﻿using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using ToyDb.Messages;

namespace ToyDb.Models;

public class DatabaseEntry
{
    public required Timestamp Timestamp { get; set; }
    public required string Key { get; set; }
    public required DataType Type { get; set; }
    public required ByteString? Data { get; set; }

    public static DatabaseEntry Null(string key) => new () {
        Timestamp = Timestamp.FromDateTime(DateTime.MinValue),
        Key = key,
        Type = DataType.Null,
        Data = null
    };
}
