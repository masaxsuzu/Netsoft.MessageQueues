using Microsoft.Extensions.Logging.Abstractions;

using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Infrastructure;
using Netsoft.MessageQueues.Runtime.Remote;

namespace Netsoft.MessageQueues.Runtime.Tests;

public sealed class RemoteSubscriptionTests : IDisposable
{
    private static readonly Topic Orders = Topic.From("orders");
    private static readonly SubscriptionName Billing = SubscriptionName.From("billing");
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TemporaryDatabase _database = new();
    private readonly RemoteSubscriptionHub _hub = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task 遠隔購読へ発行したメッセージは接続した消費者に届く()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, lanes: 1);

        await RunEngineAsync(engine, async () =>
        {
            using RemoteConsumer consumer = _hub.Attach(Orders, Billing, 0);

            MessageId id = await publisher.PublishAsync(
                Orders, MessagePayload.From("""{"orderId":42}"""), CancellationToken.None);

            RemoteDelivery delivery = await ReadAsync(consumer);

            Assert.Equal(id, delivery.Message.Id);
            Assert.Equal("""{"orderId":42}""", delivery.Message.Payload.Json);
            Assert.Equal(1, delivery.Attempt);
        });
    }

    [Fact]
    public async Task 確認するまで配送済みにならない()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, lanes: 1);

        await RunEngineAsync(engine, async () =>
        {
            using RemoteConsumer consumer = _hub.Attach(Orders, Billing, 0);
            MessageId id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);

            RemoteDelivery delivery = await ReadAsync(consumer);

            // 受け取っただけの時点では、ディスクの上ではまだ未配送。
            Assert.Equal(DeliveryStatus.Pending, await StatusOfAsync(store, id));

            Assert.True(consumer.Ack(delivery.Message.Id));
            await WaitForStatusAsync(store, id, DeliveryStatus.Delivered);
        });
    }

    [Fact]
    public async Task 失敗を返した配送は再配送される()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, lanes: 1);

        await RunEngineAsync(engine, async () =>
        {
            using RemoteConsumer consumer = _hub.Attach(Orders, Billing, 0);
            MessageId id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);

            RemoteDelivery first = await ReadAsync(consumer);
            Assert.True(consumer.Nack(first.Message.Id, "テストのための故意の失敗"));

            RemoteDelivery second = await ReadAsync(consumer);
            Assert.Equal(id, second.Message.Id);
            Assert.Equal(2, second.Attempt);
        });
    }

    [Fact]
    public async Task 確認される前に接続が切れた配送は再配送される()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, lanes: 1);

        await RunEngineAsync(engine, async () =>
        {
            MessageId id;

            // 受け取ってから確認せずに切る。プロセスが落ちた消費者の相当。
            using (RemoteConsumer consumer = _hub.Attach(Orders, Billing, 0))
            {
                id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);
                _ = await ReadAsync(consumer);
            }

            Assert.Equal(DeliveryStatus.Pending, await StatusOfAsync(store, id));

            using RemoteConsumer next = _hub.Attach(Orders, Billing, 0);
            RemoteDelivery redelivered = await ReadAsync(next);

            Assert.Equal(id, redelivered.Message.Id);
            Assert.Equal(2, redelivered.Attempt);
        });
    }

    [Fact]
    public async Task 消費者が居ない間に発行されたメッセージは接続した時点で届く()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, lanes: 1);

        await RunEngineAsync(engine, async () =>
        {
            // 繋がっていないのは異常ではない。購読は接続とは無関係に存在し続ける。
            MessageId first = await publisher.PublishAsync(Orders, MessagePayload.From("1"), CancellationToken.None);
            MessageId second = await publisher.PublishAsync(Orders, MessagePayload.From("2"), CancellationToken.None);

            using RemoteConsumer consumer = _hub.Attach(Orders, Billing, 0);

            RemoteDelivery one = await ReadAsync(consumer);
            Assert.Equal(first, one.Message.Id);
            Assert.True(consumer.Ack(one.Message.Id));

            RemoteDelivery two = await ReadAsync(consumer);
            Assert.Equal(second, two.Message.Id);
        });
    }

    [Fact]
    public void 同じレーンに2つ目の消費者は接続できない()
    {
        using RemoteConsumer first = _hub.Attach(Orders, Billing, 0);

        Assert.Throws<RemoteConsumerConflictException>(() => _hub.Attach(Orders, Billing, 0));
    }

    [Fact]
    public void 別のレーンなら同時に接続できる()
    {
        using RemoteConsumer first = _hub.Attach(Orders, Billing, 0);
        using RemoteConsumer second = _hub.Attach(Orders, Billing, 1);
    }

    [Fact]
    public void 接続を切れば同じレーンへ繋ぎ直せる()
    {
        _hub.Attach(Orders, Billing, 0).Dispose();

        using RemoteConsumer next = _hub.Attach(Orders, Billing, 0);
    }

    [Fact]
    public void 消費者が居ないレーンへの確認は受け付けない()
    {
        Assert.False(_hub.Ack(Orders, Billing, 0, MessageId.From("msg-1")));
        Assert.False(_hub.Nack(Orders, Billing, 0, MessageId.From("msg-1"), null));
    }

    [Fact]
    public async Task 渡した配送と識別子が違う確認は受け付けない()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, lanes: 1);

        await RunEngineAsync(engine, async () =>
        {
            using RemoteConsumer consumer = _hub.Attach(Orders, Billing, 0);
            MessageId id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);
            _ = await ReadAsync(consumer);

            Assert.False(_hub.Ack(Orders, Billing, 0, MessageId.From("別のメッセージ")));

            // 本物の識別子なら通る（拒否したのが識別子の違いだけであることの確認）。
            Assert.True(_hub.Ack(Orders, Billing, 0, id));
        });
    }

    [Fact]
    public async Task 遠隔購読でも同じキーは同じレーンへ落ちて発行順に届く()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, lanes: 4);
        PartitionKey key = PartitionKey.From("order-42");
        int lane = Lane.IndexFor(key.Hash, 4);

        await RunEngineAsync(engine, async () =>
        {
            using RemoteConsumer consumer = _hub.Attach(Orders, Billing, lane);

            List<MessageId> published = [];
            for (int i = 0; i < 3; i++)
            {
                published.Add(await publisher.PublishAsync(
                    Orders, MessagePayload.From($"{i}"), key, CancellationToken.None));
            }

            foreach (MessageId expected in published)
            {
                RemoteDelivery delivery = await ReadAsync(consumer);
                Assert.Equal(expected, delivery.Message.Id);
                Assert.True(consumer.Ack(delivery.Message.Id));
            }
        });
    }

    [Fact]
    public async Task プロセス内の購読者と遠隔購読は同じトピックに共存できる()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber inProcess = new("orders", "audit");
        RemoteSubscriber remote = new(Orders, Billing, 1, _hub);
        SubscriberRegistry registry = new([inProcess, remote]);
        MessageQueueSignal signal = new();
        MessagePublisher publisher = new(store, registry, signal, TimeProvider.System);
        DeliveryEngine engine = new(
            store,
            registry,
            signal,
            new MessageQueueOptions { RetryDelay = TimeSpan.FromMilliseconds(25) },
            TimeProvider.System,
            NullLogger<DeliveryEngine>.Instance);

        await RunEngineAsync(engine, async () =>
        {
            using RemoteConsumer consumer = _hub.Attach(Orders, Billing, 0);

            MessageId id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);

            Assert.Equal(id, (await inProcess.NextAsync()).Message.Id);
            Assert.Equal(id, (await ReadAsync(consumer)).Message.Id);
        });
    }

    private (MessagePublisher Publisher, DeliveryEngine Engine) Build(SqliteMessageStore store, int lanes)
    {
        RemoteSubscriber subscriber = new(Orders, Billing, lanes, _hub);
        SubscriberRegistry registry = new([subscriber]);
        MessageQueueSignal signal = new();

        // 再配送の待ちを短くする。既定の 5 秒はテストの時間予算に対して長すぎる。
        MessageQueueOptions options = new() { RetryDelay = TimeSpan.FromMilliseconds(25) };

        MessagePublisher publisher = new(store, registry, signal, TimeProvider.System);
        DeliveryEngine engine = new(
            store, registry, signal, options, TimeProvider.System, NullLogger<DeliveryEngine>.Instance);
        return (publisher, engine);
    }

    /// <summary>エンジンを走らせたまま本文を実行し、必ず停止まで面倒を見る。</summary>
    private static async Task RunEngineAsync(DeliveryEngine engine, Func<Task> body)
    {
        using CancellationTokenSource stop = new();
        Task run = engine.RunAsync(stop.Token);
        try
        {
            await body();
        }
        finally
        {
            stop.Cancel();

            // 停止は正常完了の契約（DeliveryEngine.RunAsync）。例外ならここで顕在化する。
            await run;
        }
    }

    private static async Task<RemoteDelivery> ReadAsync(RemoteConsumer consumer)
    {
        using CancellationTokenSource timeout = new(Timeout);
        return await consumer.ReadAsync(timeout.Token);
    }

    private static async Task<DeliveryStatus> StatusOfAsync(SqliteMessageStore store, MessageId id)
    {
        IReadOnlyList<Delivery> deliveries = await store.GetDeliveriesAsync(id, CancellationToken.None);
        return Assert.Single(deliveries).Status;
    }

    private static async Task WaitForStatusAsync(SqliteMessageStore store, MessageId id, DeliveryStatus status)
    {
        using CancellationTokenSource timeout = new(Timeout);
        while (await StatusOfAsync(store, id) != status)
        {
            await Task.Delay(PollInterval, timeout.Token);
        }
    }
}
