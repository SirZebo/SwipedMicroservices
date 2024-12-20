using k8s;
using k8s.Models;
using k8s.Exceptions;
using Microsoft.Rest;
using System.Net;
using System;
using System.Threading;
using System.Threading.Tasks;

public class LeaderElection
{
    private static readonly string LeaseName = "bidding-service-lease";
    private static readonly string Namespace = "default";
    private static readonly string HolderIdentity = $"{Environment.MachineName}-{Guid.NewGuid()}";
    private static readonly int LeaseDurationSeconds = 15;
    private static readonly int RenewIntervalSeconds = 5;
    
    private readonly IKubernetes _client;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private bool _isLeader;

    public LeaderElection(IKubernetes client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cancellationTokenSource = new CancellationTokenSource();
        _isLeader = false;
    }

    public bool IsLeader => _isLeader;

    public async Task StartAsync(Action? onBecameLeader = null, Action? onLostLeadership = null)
    {
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var wasLeader = _isLeader;
                    _isLeader = await TryAcquireOrRenewLease().ConfigureAwait(false);

                    if (_isLeader && !wasLeader)
                    {
                        Console.WriteLine($"Instance {HolderIdentity} became leader");
                        onBecameLeader?.Invoke();
                    }
                    else if (!_isLeader && wasLeader)
                    {
                        Console.WriteLine($"Instance {HolderIdentity} lost leadership");
                        onLostLeadership?.Invoke();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(RenewIntervalSeconds), _cancellationTokenSource.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"Error in leader election loop: {ex}");
                    _isLeader = false;
                    await Task.Delay(TimeSpan.FromSeconds(RenewIntervalSeconds), _cancellationTokenSource.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Leader election stopped");
            throw;
        }
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
    }

    private async Task<bool> TryAcquireOrRenewLease()
    {
        try
        {
            V1Lease? lease;
            try
            {
                lease = await _client.ReadNamespacedLeaseAsync(
                    name: LeaseName,
                    namespaceParameter: Namespace,
                    cancellationToken: _cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpOperationException httpEx && 
                                     httpEx.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                // Lease doesn't exist, create it
                lease = await CreateNewLease().ConfigureAwait(false);
                return true;
            }

            if (lease == null)
            {
                lease = await CreateNewLease().ConfigureAwait(false);
                return true;
            }

            if (lease.Spec?.HolderIdentity == HolderIdentity)
            {
                // We hold the lease, renew it
                if (lease.Spec != null)
                {
                    lease.Spec.RenewTime = DateTime.UtcNow;
                    await _client.ReplaceNamespacedLeaseAsync(
                        body: lease,
                        name: LeaseName,
                        namespaceParameter: Namespace,
                        cancellationToken: _cancellationTokenSource.Token).ConfigureAwait(false);
                    return true;
                }
            }

            // Check if the lease has expired
            var leaseExpiry = lease.Spec?.RenewTime?.AddSeconds(lease.Spec?.LeaseDurationSeconds ?? LeaseDurationSeconds) 
                ?? DateTime.MinValue;
            
            if (DateTime.UtcNow > leaseExpiry)
            {
                // Lease has expired, try to take it
                if (lease.Spec == null)
                {
                    lease.Spec = new V1LeaseSpec();
                }
                
                lease.Spec.HolderIdentity = HolderIdentity;
                lease.Spec.LeaseDurationSeconds = LeaseDurationSeconds;
                lease.Spec.RenewTime = DateTime.UtcNow;
                lease.Spec.AcquireTime = DateTime.UtcNow;

                await _client.ReplaceNamespacedLeaseAsync(
                    body: lease,
                    name: LeaseName,
                    namespaceParameter: Namespace,
                    cancellationToken: _cancellationTokenSource.Token).ConfigureAwait(false);
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"Error while acquiring/renewing lease: {ex}");
            return false;
        }
    }

    private async Task<V1Lease> CreateNewLease()
    {
        var lease = new V1Lease
        {
            Metadata = new V1ObjectMeta
            {
                Name = LeaseName,
                NamespaceProperty = Namespace
            },
            Spec = new V1LeaseSpec
            {
                HolderIdentity = HolderIdentity,
                LeaseDurationSeconds = LeaseDurationSeconds,
                AcquireTime = DateTime.UtcNow,
                RenewTime = DateTime.UtcNow
            }
        };

        return await _client.CreateNamespacedLeaseAsync(
            body: lease,
            namespaceParameter: Namespace,
            cancellationToken: _cancellationTokenSource.Token).ConfigureAwait(false);
    }
}

// Example usage:
public class LeaderProgram
{
    public static async Task Main(string[] args)
    {
        var config = KubernetesClientConfiguration.InClusterConfig();
        IKubernetes client = new Kubernetes(config);
        
        var leaderElection = new LeaderElection(client);
        
        await leaderElection.StartAsync(
            onBecameLeader: () =>
            {
                Console.WriteLine("This instance is now the leader!");
                // Start leader-specific tasks
            },
            onLostLeadership: () =>
            {
                Console.WriteLine("This instance is no longer the leader!");
                // Stop leader-specific tasks
            }
        ).ConfigureAwait(false);
    }
}