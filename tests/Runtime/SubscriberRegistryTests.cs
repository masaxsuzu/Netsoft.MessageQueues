using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime.Tests;

public sealed class SubscriberRegistryTests
{
    [Fact]
    public void 同じトピックに同じ購読名は登録できない()
    {
        RecordingSubscriber first = new("orders", "billing");
        RecordingSubscriber second = new("orders", "billing");

        Assert.Throws<ArgumentException>(() => new SubscriberRegistry([first, second]));
    }

    [Fact]
    public void 別のトピックなら同じ購読名を使える()
    {
        SubscriberRegistry registry = new([
            new RecordingSubscriber("orders", "audit"),
            new RecordingSubscriber("payments", "audit"),
        ]);

        Assert.Equal(2, registry.All.Count);
    }

    [Fact]
    public void トピックか購読名が空の購読者は登録できない()
    {
        Assert.Throws<ArgumentException>(() => new SubscriberRegistry([
            new BareSubscriber { Topic = default, Name = SubscriptionName.From("billing") },
        ]));
        Assert.Throws<ArgumentException>(() => new SubscriberRegistry([
            new BareSubscriber { Topic = Topic.From("orders"), Name = default },
        ]));
    }

    [Fact]
    public void Forはそのトピックの購読者だけを返す()
    {
        RecordingSubscriber billing = new("orders", "billing");
        RecordingSubscriber audit = new("orders", "audit");
        RecordingSubscriber payments = new("payments", "ledger");
        SubscriberRegistry registry = new([billing, audit, payments]);

        IReadOnlyList<IMessageSubscriber> subscribers = registry.For(Topic.From("orders"));

        Assert.Equal(2, subscribers.Count);
        Assert.Contains(billing, subscribers);
        Assert.Contains(audit, subscribers);
    }

    [Fact]
    public void 購読者が居ないトピックのForは空を返す()
    {
        SubscriberRegistry registry = new([new RecordingSubscriber("orders", "billing")]);

        Assert.Empty(registry.For(Topic.From("payments")));
    }

    private sealed class BareSubscriber : IMessageSubscriber
    {
        public Topic Topic { get; init; }

        public SubscriptionName Name { get; init; }

        public Task HandleAsync(Message message, int attempt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
