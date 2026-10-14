using System;
using System.Collections.Generic;
using System.Threading;
using NSubstitute;
using Reown.Sign.Controllers;
using Reown.Sign.Interfaces;
using Reown.Sign.Models.Engine.Events;
using Xunit;

namespace Reown.Sign.Test;

[Trait("Category", "unit")]
public class AddressProviderTests
{
    [Fact]
    public void ASessionDeletedWithNoDefaultSetDoesNotThrow()
    {
        var client = Substitute.For<ISignClient>();
        client.Session.Returns(Substitute.For<ISession>());

        using var provider = new AddressProvider(client);
        Assert.False(provider.HasDefaultSession);

        // The handlers are async void, so an exception in one does not surface to whoever raised the
        // event — it is posted to the synchronization context, and with none installed it reaches the
        // thread pool and ends the process. Capture it instead.
        var exceptions = RaiseAndCapture(() =>
            client.SessionDeleted += Raise.Event<EventHandler<SessionEvent>>(client, new SessionEvent { Topic = "topic-a" }));

        Assert.Empty(exceptions);
    }

    [Fact]
    public void ASessionUpdatedWithNoDefaultSetDoesNotThrow()
    {
        var client = Substitute.For<ISignClient>();
        client.Session.Returns(Substitute.For<ISession>());

        using var provider = new AddressProvider(client);

        var exceptions = RaiseAndCapture(() =>
            client.SessionUpdateRequest += Raise.Event<EventHandler<SessionUpdateEvent>>(client, new SessionUpdateEvent { Topic = "topic-a" }));

        Assert.Empty(exceptions);
    }

    private static IReadOnlyList<Exception> RaiseAndCapture(Action raise)
    {
        var context = new CapturingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            raise();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        return context.Captured;
    }

    private sealed class CapturingSynchronizationContext : SynchronizationContext
    {
        public readonly List<Exception> Captured = new();

        public override void Post(SendOrPostCallback d, object state)
        {
            try
            {
                d(state);
            }
            catch (Exception e)
            {
                Captured.Add(e);
            }
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            Post(d, state);
        }
    }
}
