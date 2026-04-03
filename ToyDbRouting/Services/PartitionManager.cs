using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using ToyDbRouting.Clients;
using ToyDbRouting.Models;

namespace ToyDbRouting.Services;

public class PartitionManager(
    IOptions<RoutingOptions> routingOptions,
    ILogger<PartitionManager> logger,
    ConsistentHashRing ring
) : BackgroundService
{
    private readonly RoutingOptions _options = routingOptions.Value;
    private TimeSpan _discoveryInterval;
    private readonly ConsistentHashRing _ring = ring;
    private readonly Dictionary<string, ReplicaClient> _replicaClients = [];

    private readonly ConcurrentDictionary<string, ReplicaClient> _primaryReplicas = new();
    private readonly ConcurrentDictionary<string, bool> _pendingRediscovery = new();

    public IReadOnlyDictionary<string, ReplicaClient> PrimaryReplicas => _primaryReplicas;

    public virtual void TriggerRediscovery(string partitionId)
    {
        if (_pendingRediscovery.TryAdd(partitionId, true))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await DiscoverSinglePartitionPrimaryAsync(partitionId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error triggering rediscovery for partition {PartitionId}", partitionId);
                }
                finally
                {
                    _pendingRediscovery.TryRemove(partitionId, out _);
                }
            });
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _discoveryInterval = TimeSpan.FromSeconds(_options.PrimaryDiscoveryIntervalSeconds);

        InitializeReplicaClients();

        await DiscoverPrimaryReplicasAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DiscoverPrimaryReplicasAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during primary discovery");
            }

            await Task.Delay(_discoveryInterval, stoppingToken);
        }
    }

    private void InitializeReplicaClients()
    {
        foreach (var partition in _options.Partitions)
        {
            if (!_replicaClients.ContainsKey(partition.PrimaryReplicaAddress))
            {
                _replicaClients[partition.PrimaryReplicaAddress] = new ReplicaClient(partition.PrimaryReplicaAddress);
            }

            foreach (var secondaryAddress in partition.SecondaryReplicaAddresses)
            {
                if (!_replicaClients.ContainsKey(secondaryAddress))
                {
                    _replicaClients[secondaryAddress] = new ReplicaClient(secondaryAddress);
                }
            }
        }
    }

    private async Task DiscoverPrimaryReplicasAsync(CancellationToken cancellationToken)
    {
        foreach (var partitionConfig in _options.Partitions)
        {
            await DiscoverSinglePartitionPrimaryAsync(partitionConfig.PartitionId, cancellationToken);
        }
    }

    private async Task DiscoverSinglePartitionPrimaryAsync(string partitionId, CancellationToken cancellationToken)
    {
        var partitionConfig = _options.Partitions.FirstOrDefault(p => p.PartitionId == partitionId);
        if (partitionConfig == null) return;

        var allReplicas = new[] { partitionConfig.PrimaryReplicaAddress }.Concat(partitionConfig.SecondaryReplicaAddresses);

        ReplicaClient? newPrimary = null;

        foreach (var address in allReplicas)
        {
            if (!_replicaClients.TryGetValue(address, out var client))
                continue;

            try
            {
                using var clusterClient = new Clients.ClusterClient(address);
                var role = await clusterClient.GetRole(cancellationToken);

                if (role.IsPrimary)
                {
                    newPrimary = client;
                    break;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get role from replica {Address}", address);
            }
        }

        if (newPrimary != null)
        {
            _primaryReplicas[partitionConfig.PartitionId] = newPrimary;
            UpdatePartitionPrimary(partitionConfig.PartitionId, newPrimary);
        }
        else if (!_primaryReplicas.ContainsKey(partitionConfig.PartitionId))
        {
            logger.LogWarning("No primary found for partition {PartitionId}, using configured primary", partitionConfig.PartitionId);
            if (!_replicaClients.TryGetValue(partitionConfig.PrimaryReplicaAddress, out var fallbackPrimary))
                fallbackPrimary = new ReplicaClient(partitionConfig.PrimaryReplicaAddress);
            _primaryReplicas[partitionConfig.PartitionId] = fallbackPrimary;
            UpdatePartitionPrimary(partitionConfig.PartitionId, fallbackPrimary);
        }
    }

    private void UpdatePartitionPrimary(string partitionId, ReplicaClient newPrimary)
    {
        var partition = _ring.Partitions.FirstOrDefault(p => p.PartitionId == partitionId);
        partition?.UpdatePrimary(newPrimary);
    }
}