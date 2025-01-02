using Google.Protobuf;

namespace ToyDb.Models;

public static class NullMarker
{
    private static readonly ByteString _marker = ByteString.CopyFromUtf8("null");

    public static string ToBase64() => _marker.ToBase64();
    
    public static bool Equals(ByteString? value) => _marker.Equals(value);
}
