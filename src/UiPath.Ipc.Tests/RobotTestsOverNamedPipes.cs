using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using UiPath.Ipc.Transport.NamedPipe;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class RobotTestsOverNamedPipes : RobotTests
{
    private string PipeName => Names.GetPipeName(role: "robot", TestRunId);

    public RobotTestsOverNamedPipes(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected override async Task<ServerTransport> CreateServerTransport() => new NamedPipeServerTransport
    {
        PipeName = PipeName
    };
    protected override ClientTransport CreateClientTransport() => new NamedPipeClientTransport
    {
        PipeName = PipeName,
        AllowImpersonation = true,
    };


    [Fact]
    public async Task CommandLineTest()
    {
        var lazyProxy = new Lazy<IStudioOperations>(() => StudioOperationsProxyFactory.Create(PipeName));
        await lazyProxy.Value.GetRobotInfoCore(message: new());
        await lazyProxy.Value.SetOffline(true);
    }

    private static class StudioOperationsProxyFactory
    {
        public static IStudioOperations Create(string pipeName, IStudioEvents events = null!) => Communication.GivenCallback(events ?? EmptyStudioEvents.Instance).CreateUserServiceProxy<IStudioOperations>(pipeName);

        class EmptyStudioEvents : IStudioEvents
        {
            public static readonly EmptyStudioEvents Instance = new();

            public Task OnRobotInfoChanged(RobotInfoChangedArgs args) => Task.CompletedTask;
        }
    }

    private static class Communication
    {
        public static CallbackInstance<TCallback> GivenCallback<TCallback>(TCallback callback) where TCallback : class => new(callback);

        public static void OnConnectingToUserService()
        {
            // do nothing
        }
    }

    public readonly struct CallbackInstance<TCallback> where TCallback : class
    {
        public TCallback Instance { get; }

        public CallbackInstance(TCallback instance) => Instance = instance;

        public TContract CreateUserServiceProxy<TContract>(string pipeName)
            where TContract : class
        => RobotIpcHelpers.CreateProxy<TContract>(
            pipeName,
            requestTimeout: TimeSpan.FromSeconds(40),
            callbacks: new ContractCollection()
            {
                { typeof(TCallback), Instance }
            },
            beforeConnect: Communication.OnConnectingToUserService);
    }
}

internal static partial class RobotIpcHelpers
{
    private static readonly ConcurrentDictionary<CreateProxyRequest, ClientAndParams> PipeClients = new();

    public static TContract CreateProxy<TContract>(
        string pipeName,
        TimeSpan? requestTimeout = null,
        ContractCollection? callbacks = null,
        IServiceProvider? provider = null,
        Action? beforeConnect = null,
        BeforeCallHandler? beforeCall = null,
        bool allowImpersonation = false,
        TaskScheduler? scheduler = null) where TContract : class
    {
        // TODO: Fix this
        // Dirty hack (temporary): different callback sets will result in different connections
        // Hopefully, the different sets are also disjunctive.

        // We're still making sure that beforeConnect, beforeCall, scheduler and requestTimeout are the same.
        // If that happens, and exception will be thrown.

        // If the sets are indeed disjunctive, and they should, since the original API provided on callback type per service type,
        // then the server will not erroneously inflate the number of client callbacks.

        // What might happen invisibly is that beforeConnect will be called more than once, but that's hopefully idempotent.
        var actualKey = new Key(
            pipeName,
            allowImpersonation,
            EquatableEndpointSet.From(callbacks, haveProvider: provider is not null));

        Params requestedParams = new(
            requestTimeout,
            provider,
            scheduler,
            beforeCall,
            beforeConnect);

        var (client, originalParams) = PipeClients.GetOrAdd(
            new(actualKey, requestedParams, callbacks),
            CreateClient);

        if (requestedParams != originalParams)
        {
            throw requestedParams - originalParams;
        }

        return client.GetProxy<TContract>();
    }

    private static ClientAndParams CreateClient(CreateProxyRequest request)
    => new(
        Client: new()
        {
            RequestTimeout = request.Params.RequestTimeout ?? Timeout.InfiniteTimeSpan,
            ServiceProvider = request.Params.Provider,
            Logger = request.Params.Provider?.GetService<ILogger<IpcProxy>>(),
            Callbacks = request.Callbacks,
            BeforeConnect = request.Params.BeforeConnect is null ? null : _ =>
            {
                request.Params.BeforeConnect();
                return Task.CompletedTask;
            },
            BeforeOutgoingCall = request.Params.BeforeCall,
            Scheduler = request.Params.Scheduler,
            Transport = new NamedPipeClientTransport
            {
                PipeName = request.ActualKey.Name,
                AllowImpersonation = request.ActualKey.AllowImpersonation,
            },
        },
        request.Params);

    internal readonly record struct Key(string Name, bool AllowImpersonation, EquatableEndpointSet Callbacks);
    internal readonly record struct Params(
        TimeSpan? RequestTimeout,
        IServiceProvider? Provider,
        TaskScheduler? Scheduler,
        BeforeCallHandler? BeforeCall,
        Action? BeforeConnect)
    {
        public static Exception? operator -(Params @new, Params old)
        {
            var differences = EnumerateDifferences().ToArray();
            if (differences.Length is 0) return null;
            return new InvalidOperationException($"{nameof(Params)} differences:\r\n{string.Join("\r\n", differences)}");

            IEnumerable<string> EnumerateDifferences()
            {
                if (@new.RequestTimeout != old.RequestTimeout)
                {
                    yield return Compose(nameof(RequestTimeout), @new.RequestTimeout, old.RequestTimeout);
                }
                if (AreDifferent(@new.Provider, old.Provider))
                {
                    yield return Compose(nameof(Provider), @new.Provider, old.Provider);
                }
                if (AreDifferent(@new.Scheduler, old.Scheduler))
                {
                    yield return Compose(nameof(Scheduler), @new.Scheduler, old.Scheduler);
                }
                if (AreDifferent(@new.BeforeCall, old.BeforeCall))
                {
                    yield return Compose(nameof(BeforeCall), @new.BeforeCall, old.BeforeCall);
                }
                if (AreDifferent(@new.BeforeConnect, old.BeforeConnect))
                {
                    yield return Compose(nameof(BeforeConnect), @new.BeforeConnect, old.BeforeConnect);
                }
            }

            static bool AreDifferent<T>(T? @new, T? old) where T : class
            {
                if (@new is null && old is null)
                {
                    return false;
                }
                if (@new is null || old is null)
                {
                    return true;
                }
                return !@new.Equals(old);
            }

            static string Compose<T>(string name, T @new, T old)
            => $"New {name} is {@new?.ToString() ?? "null"} but was originally {old?.ToString() ?? "null"}.";
        }
    }
    internal readonly record struct CreateProxyRequest(Key ActualKey, Params Params, ContractCollection? Callbacks)
    {
        public bool Equals(CreateProxyRequest other) => ActualKey.Equals(other.ActualKey);
        public override int GetHashCode() => ActualKey.GetHashCode();
    }
    internal readonly record struct ClientAndParams(IpcClient Client, Params Params);

    internal readonly struct EquatableEndpointSet : IEquatable<EquatableEndpointSet>
    {
        public static EquatableEndpointSet From(ContractCollection? endpoints, bool haveProvider)
        {
            return Pal(endpoints?.AsEnumerable(), haveProvider);

            static EquatableEndpointSet Pal(IEnumerable<ContractSettings>? endpoints, bool haveProvider)
            {
                var items = endpoints?.AsEnumerable();

                // Dirty fix (temporary):
                // Reduce the chance of difference callback sets by removing null callback instances when there'n no service provider.
                // If the Robot's maturity says anything about such situations it that the server will not use those callbacks.
                if (!haveProvider)
                {
                    items = items?.Where(callback => callback.ServiceInstance is not null);
                }

                var set = items?.ToHashSet();

                // reuse the cached empty set for null callback sets, empty callback sets or non-empty callback sets that result in being empty after the removal.
                if (set is not { Count: > 0 })
                {
                    return Empty;
                }

                return new(set);
            }
        }

        public static readonly EquatableEndpointSet Empty = new([]);
        private readonly HashSet<ContractSettings> _set;

        private EquatableEndpointSet(HashSet<ContractSettings> set) => _set = set;

        public bool Equals(EquatableEndpointSet other) => _set.SetEquals(other._set);
        public override bool Equals(object? obj) => obj is EquatableEndpointSet other && Equals(other);
        public override int GetHashCode() => _set.Count;

        public override string ToString()
        {
            return $"[{string.Join(", ", _set.Select(Pal))}]";
            static string Pal(ContractSettings endpointSettings)
            => $"{endpointSettings.ContractType.Name},sp:{RuntimeHelpers.GetHashCode(endpointSettings.ServiceProvider)},instance:{RuntimeHelpers.GetHashCode(endpointSettings.ServiceInstance)}";
        }
    }

}

internal static class HashSetExtensions
{
    public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source) => source.ToHashSet(null);

    public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource>? comparer)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (source is HashSet<TSource> existingHashSet)
        {
            return existingHashSet;
        }

        return new HashSet<TSource>(source, comparer);
    }
}