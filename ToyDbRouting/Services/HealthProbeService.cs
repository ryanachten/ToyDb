using Grpc.Net.Client;
using Grpc.Health.V1;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using ToyDbRouting.Models;

namespace ToyDbRouting.Services;

public class HealthProbeService : BackgroundService
{
    private readonly RoutingOptions _routingOptions;
    private readonly ILogger<HealthProbeService> _logger;
    private readonly ConcurrentDictionary<string, HealthCheckResponse.Types.ServingStatus> _healthStates = new();
    private readonly TimeSpan _probeInterval;
    private readonly ConcurrentDictionary<string, Health.HealthClient> _healthClients = new();

    public HealthProbeService(ILogger<HealthProbeService> logger, IOptions<RoutingOptions> routingOptions)
    {
        _logger = logger;
        _routingOptions = routingOptions.Value;
        
        if (_routingOptions.HealthProbeIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routingOptions), "HealthProbeIntervalSeconds must be greater than zero.");
        }

        _probeInterval = TimeSpan.FromSeconds(_routingOptions.HealthProbeIntervalSeconds);

        var addresses = _routingOptions.Partitions
            .Select(p => p.PrimaryReplicaAddress)
            .Concat(_routingOptions.Partitions.SelectMany(p => p.SecondaryReplicaAddresses))
            .Distinct();

        foreach (var address in addresses)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = handler });
            _healthClients[address] = new Health.HealthClient(channel);
        }
    }

    public IReadOnlyDictionary<string, HealthCheckResponse.Types.ServingStatus> HealthStates => _healthStates;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var partition in _routingOptions.Partitions)
            {
                await ProbeReplica(partition.PrimaryReplicaAddress, stoppingToken);
                foreach (var secondary in partition.SecondaryReplicaAddresses)
                    await ProbeReplica(secondary, stoppingToken);
            }
            await Task.Delay(_probeInterval, stoppingToken);
        }
    }

    private async Task ProbeReplica(string address, CancellationToken stoppingToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(_probeInterval);

            var healthClient = _healthClients[address];
            var response = await healthClient.CheckAsync(new HealthCheckRequest(), cancellationToken: timeoutCts.Token);
            var status = response.Status;
            var previous = _healthStates.GetOrAdd(address, status);
            if (previous != status)
            {
                _healthStates[address] = status;
                _logger.LogInformation("Health status changed for {Address}: {Previous} -> {Status}", address, previous, status);
            }
        }
        catch (Exception ex)
        {
            var previous = _healthStates.GetOrAdd(address, HealthCheckResponse.Types.ServingStatus.Unknown);
            if (previous != HealthCheckResponse.Types.ServingStatus.NotServing)
            {
                _healthStates[address] = HealthCheckResponse.Types.ServingStatus.NotServing;
                _logger.LogWarning("Health check failed for {Address}: {Message}", address, ex.Message);
            }
        }
    }
}
