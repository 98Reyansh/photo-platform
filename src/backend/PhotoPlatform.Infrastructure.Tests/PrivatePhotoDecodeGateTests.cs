using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure;
using PhotoPlatform.Infrastructure.Photos;

namespace PhotoPlatform.Infrastructure.Tests;

public class PrivatePhotoDecodeGateTests
{
    [Fact]
    public async Task Overlapping_decodes_do_not_run_together()
    {
        var events = new List<string>();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var turn = 0;
        var gate = Gate(content =>
        {
            var which = Interlocked.Increment(ref turn);
            Add(events, "enter-" + which);
            if (which == 1)
            {
                firstEntered.TrySetResult();
                releaseFirst.Task.GetAwaiter().GetResult();
            }

            Add(events, "exit-" + which);
            return new PrivatePhotoDecodeResult.Rejected(PrivatePhotoContentDecision.Malformed);
        });

        var first = Start(gate, CancellationToken.None);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Start(gate, CancellationToken.None);
        releaseFirst.TrySetResult();

        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["enter-1", "exit-1", "enter-2", "exit-2"], events);
    }

    [Fact]
    public async Task Cancelling_a_waiting_decode_does_not_release_the_active_permit()
    {
        var events = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var turn = 0;
        var gate = Gate(content =>
        {
            var which = Interlocked.Increment(ref turn);
            Add(events, "enter-" + which);
            if (which == 1)
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
            }

            Add(events, "exit-" + which);
            return new PrivatePhotoDecodeResult.Rejected(PrivatePhotoContentDecision.Empty);
        });

        var first = Start(gate, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var waiting = Start(gate, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await gate.DecodeAsync(new byte[] { 1 }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["enter-1", "exit-1", "enter-2", "exit-2"], events);
    }

    [Fact]
    public async Task A_rejected_decode_releases_the_permit()
    {
        var gate = Gate(content => new PrivatePhotoDecodeResult.Rejected(PrivatePhotoContentDecision.Malformed));

        var first = await gate.DecodeAsync(new byte[] { 1 }, CancellationToken.None);
        var second = await gate.DecodeAsync(new byte[] { 1 }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<PrivatePhotoDecodeResult.Rejected>(first);
        Assert.IsType<PrivatePhotoDecodeResult.Rejected>(second);
    }

    [Fact]
    public async Task A_thrown_decode_releases_the_permit_for_a_later_call()
    {
        var calls = 0;
        var gate = Gate(content =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("decode failed");
            }

            return new PrivatePhotoDecodeResult.Rejected(PrivatePhotoContentDecision.Empty);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.DecodeAsync(new byte[] { 1 }, CancellationToken.None));
        var later = await gate.DecodeAsync(new byte[] { 1 }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<PrivatePhotoDecodeResult.Rejected>(later);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void The_process_registers_one_shared_decode_gate()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Port=1;Database=photo_platform_test;Username=photoplatform;Password=unused",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        Assert.Equal(ServiceLifetime.Singleton, Lifetime(services, typeof(IPrivatePhotoDecodeGate)));
        Assert.Equal(ServiceLifetime.Singleton, Lifetime(services, typeof(IPrivatePhotoStore)));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime(services, typeof(IPhotoOriginalUpload)));

        using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<IPrivatePhotoDecodeGate>(),
            provider.GetRequiredService<IPrivatePhotoDecodeGate>());
        Assert.Same(
            provider.GetRequiredService<IPrivatePhotoStore>(),
            provider.GetRequiredService<IPrivatePhotoStore>());
    }

    [Fact]
    public void Decode_stays_synchronous_and_does_not_take_a_cancellation_token()
    {
        var method = typeof(IPrivatePhotoDecoder).GetMethod(nameof(IPrivatePhotoDecoder.Decode));

        Assert.NotNull(method);
        Assert.Equal(typeof(PrivatePhotoDecodeResult), method.ReturnType);
        Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(CancellationToken));
    }

    private static Task<PrivatePhotoDecodeResult> Start(IPrivatePhotoDecodeGate gate, CancellationToken cancellationToken)
    {
        return Task.Run(() => gate.DecodeAsync(new byte[] { 1 }, cancellationToken));
    }

    private static PrivatePhotoDecodeGate Gate(Func<ReadOnlyMemory<byte>, PrivatePhotoDecodeResult> decode)
    {
        return new PrivatePhotoDecodeGate(new ScriptedDecoder(decode));
    }

    private static void Add(List<string> events, string value)
    {
        lock (events)
        {
            events.Add(value);
        }
    }

    private static ServiceLifetime Lifetime(IServiceCollection services, Type serviceType)
    {
        return Assert.Single(services, service => service.ServiceType == serviceType).Lifetime;
    }

    private sealed class ScriptedDecoder(Func<ReadOnlyMemory<byte>, PrivatePhotoDecodeResult> decode) : IPrivatePhotoDecoder
    {
        public PrivatePhotoDecodeResult Decode(ReadOnlyMemory<byte> content)
        {
            return decode(content);
        }
    }
}
