using System;
using System.Threading;
using System.Threading.Tasks;
using Reown.Core;
using Xunit;

namespace Reown.Core.Network.Test;

[Trait("Category", "unit")]
public class HeartBeatTests
{
    private static readonly TimeSpan NoPulseTimeout = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Dispose_StopsFurtherPulses()
    {
        var firstPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var additionalPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pulseCount = 0;
        var heartBeat = new HeartBeat(25);
        heartBeat.OnPulse += (_, _) =>
        {
            if (Interlocked.Increment(ref pulseCount) == 1)
            {
                firstPulse.TrySetResult(true);
                return;
            }

            additionalPulse.TrySetResult(true);
        };

        await heartBeat.InitAsync();
        await WaitForCompletionAsync(firstPulse.Task);

        heartBeat.Dispose();

        await AssertDoesNotCompleteAsync(additionalPulse.Task);
        await WaitForCompletionAsync(heartBeat.PulseTask);
        Assert.False(heartBeat.PulseTask.IsFaulted);
    }

    [Fact]
    public async Task ThrowingSubscriber_DoesNotStopOtherSubscribersOrLaterPulses()
    {
        var laterPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var successfulPulseCount = 0;
        var heartBeat = new HeartBeat(25);
        heartBeat.OnPulse += (_, _) => throw new InvalidOperationException("subscriber failure");
        heartBeat.OnPulse += (_, _) =>
        {
            if (Interlocked.Increment(ref successfulPulseCount) >= 2)
            {
                laterPulse.TrySetResult(true);
            }
        };

        try
        {
            await heartBeat.InitAsync();

            await WaitForCompletionAsync(laterPulse.Task);
        }
        finally
        {
            heartBeat.Dispose();
            await WaitForCompletionAsync(heartBeat.PulseTask);
        }

        Assert.False(heartBeat.PulseTask.IsFaulted);
    }

    [Fact]
    public async Task CancellationToken_StopsFurtherPulsesWithoutFaultingTheLoop()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var firstPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var additionalPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pulseCount = 0;
        var heartBeat = new HeartBeat(25);
        heartBeat.OnPulse += (_, _) =>
        {
            if (Interlocked.Increment(ref pulseCount) == 1)
            {
                firstPulse.TrySetResult(true);
                return;
            }

            additionalPulse.TrySetResult(true);
        };

        try
        {
            await heartBeat.InitAsync(cancellationTokenSource.Token);
            await WaitForCompletionAsync(firstPulse.Task);

            cancellationTokenSource.Cancel();

            await AssertDoesNotCompleteAsync(additionalPulse.Task);
            await WaitForCompletionAsync(heartBeat.PulseTask);
        }
        finally
        {
            heartBeat.Dispose();
        }

        Assert.False(heartBeat.PulseTask.IsFaulted);
    }

    [Fact]
    public async Task InitAsync_CalledTwice_StartsOnlyOnePulseLoop()
    {
        var firstPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpectedPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirstPulse = new ManualResetEventSlim(false);
        var pulseCount = 0;
        var heartBeat = new HeartBeat(25);
        heartBeat.OnPulse += (_, _) =>
        {
            if (Interlocked.Increment(ref pulseCount) == 1)
            {
                firstPulse.TrySetResult(true);
                releaseFirstPulse.Wait();
                return;
            }

            unexpectedPulse.TrySetResult(true);
        };

        try
        {
            await heartBeat.InitAsync();
            await WaitForCompletionAsync(firstPulse.Task);

            await heartBeat.InitAsync();

            await AssertDoesNotCompleteAsync(unexpectedPulse.Task);
        }
        finally
        {
            releaseFirstPulse.Set();
            heartBeat.Dispose();
            await WaitForCompletionAsync(heartBeat.PulseTask);
        }
    }

    private static async Task AssertDoesNotCompleteAsync(Task task)
    {
        var completedTask = await Task.WhenAny(task, Task.Delay(NoPulseTimeout));

        Assert.NotSame(task, completedTask);
    }

    private static async Task WaitForCompletionAsync(Task task)
    {
        var completedTask = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(task, completedTask);
        await task;
    }
}
