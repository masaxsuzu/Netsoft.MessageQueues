using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Infrastructure;

namespace Netsoft.MessageQueues.Runtime.Tests;

public sealed class MessagePublisherTests : IDisposable
{
    private static readonly Topic Orders = Topic.From("orders");

    private readonly TemporaryDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task 購読者の居ないトピックへの発行は弾く()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        MessagePublisher publisher = NewPublisher(store, new SubscriberRegistry([]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None));
    }

    [Fact]
    public async Task 発行が返った時点で永続化は完了している()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing");
        MessagePublisher publisher = NewPublisher(store, new SubscriberRegistry([billing]));

        MessageId id = await publisher.PublishAsync(
            Orders, MessagePayload.From("""{"orderId":42}"""), CancellationToken.None);

        // エンジンを介さず、ストアを直接読んで確かめる。
        ClaimedDelivery? claimed = await store.TryClaimNextAsync(Orders, billing.Name, Lane.Single, CancellationToken.None);
        Assert.Equal(id, claimed!.Message.Id);
        Assert.Equal("""{"orderId":42}""", claimed.Message.Payload.Json);
    }

    [Fact]
    public async Task 配送行はそのトピックの購読の分だけ作られる()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing");
        RecordingSubscriber ledger = new("payments", "ledger");
        MessagePublisher publisher = NewPublisher(store, new SubscriberRegistry([billing, ledger]));

        MessageId id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);

        IReadOnlyList<Delivery> deliveries = await store.GetDeliveriesAsync(id, CancellationToken.None);
        Delivery delivery = Assert.Single(deliveries);
        Assert.Equal(billing.Name, delivery.Subscription);
    }

    [Fact]
    public async Task パーティションキー付きで発行したメッセージはキーを保って届く()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing");
        MessagePublisher publisher = NewPublisher(store, new SubscriberRegistry([billing]));
        PartitionKey key = PartitionKey.From("order-42");

        MessageId id = await publisher.PublishAsync(
            Orders, MessagePayload.From("{}"), key, CancellationToken.None);

        ClaimedDelivery? claimed = await store.TryClaimNextAsync(Orders, billing.Name, Lane.Single, CancellationToken.None);
        Assert.Equal(id, claimed!.Message.Id);
        Assert.Equal(key, claimed.Message.Key);
    }

    [Fact]
    public async Task 発行のたびに異なる識別子が払い出される()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing");
        MessagePublisher publisher = NewPublisher(store, new SubscriberRegistry([billing]));

        MessageId first = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);
        MessageId second = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);

        Assert.NotEqual(first, second);
    }

    private static MessagePublisher NewPublisher(SqliteMessageStore store, SubscriberRegistry registry) =>
        new(store, registry, new MessageQueueSignal(), TimeProvider.System);
}
