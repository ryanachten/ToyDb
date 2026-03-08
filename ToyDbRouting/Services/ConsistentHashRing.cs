using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using ToyDbRouting.Models;

namespace ToyDbRouting.Services;

public class ConsistentHashRing(IOptions<RoutingOptions> options, ILogger<ConsistentHashRing> logger)
{
    private readonly SortedDictionary<uint, Partition> _ring = InitializeRing(options.Value);
    private uint[]? _sortedKeys;

    private static SortedDictionary<uint, Partition> InitializeRing(RoutingOptions options)
    {
        var ring = new SortedDictionary<uint, Partition>();
        var virtualNodeCount = options.VirtualNodesPerPartition;

        foreach (var config in options.Partitions)
        {
            var partition = new Partition(config);
            for (int i = 0; i < virtualNodeCount; i++)
            {
                var virtualNodeKey = $"{partition.PartitionId}:{i}";
                var hash = Hash(virtualNodeKey);
                ring[hash] = partition;
            }
        }
        return ring;
    }

    public IEnumerable<Partition> Partitions => _ring.Values.Distinct();

    /// <summary>
    /// Finds the responsible partition for a given key using the consistent hash ring.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The partition responsible for the key.</returns>
    public Partition GetPartition(string key)
    {
        if (_ring.Count == 0)
        {
            throw new InvalidOperationException("No partitions available in the hash ring.");
        }

        // Cache the sorted keys for efficient binary search. 
        // We rebuild this array only when partitions are added.
        _sortedKeys ??= [.. _ring.Keys];

        var hash = Hash(key);

        // Binary search for the first key >= hash. 
        // If an exact match is found, index will be >= 0.
        int index = Array.BinarySearch(_sortedKeys, hash);

        if (index < 0)
        {
            // If the hash isn't found exactly, BinarySearch returns the bitwise complement (~) 
            // of the index of the next element that is LARGER than the search value.
            // By applying ~ again, we recover that index.
            // This effectively finds the "next" node on the ring clockwise.
            index = ~index;
        }

        // If index is equal to the length of the array, the key's hash is greater than 
        // the hash of any node on the ring. In consistent hashing, this means we 
        // "wrap around" the circle to the very first node.
        if (index == _sortedKeys.Length)
        {
            index = 0;
        }

        var partition = _ring[_sortedKeys[index]];

        logger.LogInformation("Selected partition: {Partition} for key: {Key}", partition.PartitionId, key);

        return partition;
    }

    private static uint Hash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hashBytes = XxHash32.Hash(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(hashBytes);
    }
}
