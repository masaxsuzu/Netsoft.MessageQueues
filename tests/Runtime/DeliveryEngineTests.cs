using Microsoft.Extensions.Logging.Abstractions;

using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Infrastructure;

namespace Netsoft.MessageQueues.Runtime.Tests;

public sealed class DeliveryEngineTests : IDisposable
{
    private static readonly Topic Orders = Topic.From("orders");
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TemporaryDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task 発行したメッセージは購読者に届き配送済みになる()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing");
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, [billing]);

        await RunEngineAsync(engine, async () =>
        {
            MessageId id = await publisher.PublishAsync(
                Orders, MessagePayload.From("""{"orderId":42}"""), CancellationToken.None);

            (Message message, int attempt) = await billing.NextAsync();
            Assert.Equal(id, message.Id);
            Assert.Equal("""{"orderId":42}""", message.Payload.Json);
            Assert.Equal(1, attempt);

            await WaitForStatusAsync(store, id, billing.Name, DeliveryStatus.Delivered);
        });
    }

    [Fact]
    public async Task 同じトピックの複数の購読者すべてに届く()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing");
        RecordingSubscriber audit = new("orders", "audit");
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, [billing, audit]);

        await RunEngineAsync(engine, async () =>
        {
            MessageId id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);

            (Message toBilling, _) = await billing.NextAsync();
            (Message toAudit, _) = await audit.NextAsync();
            Assert.Equal(id, toBilling.Id);
            Assert.Equal(id, toAudit.Id);
        });
    }

    [Fact]
    public async Task 別のトピックの購読者には届かない()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing");
        RecordingSubscriber ledger = new("payments", "ledger");
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, [billing, ledger]);

        await RunEngineAsync(engine, async () =>
        {
            await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);

            // 届く側が届き切った後に「届かない側」を見る。逆順は配送前に見て偽陽性になる。
            _ = await billing.NextAsync();
            Assert.True(ledger.HasReceivedNothing);
        });
    }

    [Fact]
    public async Task 失敗した配送は同じメッセージのまま再配送される()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing", failFirstAttempts: 2);
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, [billing]);

        await RunEngineAsync(engine, async () =>
        {
            MessageId id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);

            (Message message, int attempt) = await billing.NextAsync();
            Assert.Equal(id, message.Id);
            Assert.Equal(3, attempt);
        });
    }

    [Fact]
    public async Task 同じ購読への配送は発行の順を保つ()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing");
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, [billing]);

        await RunEngineAsync(engine, async () =>
        {
            MessageId first = await publisher.PublishAsync(Orders, MessagePayload.From("1"), CancellationToken.None);
            MessageId second = await publisher.PublishAsync(Orders, MessagePayload.From("2"), CancellationToken.None);
            MessageId third = await publisher.PublishAsync(Orders, MessagePayload.From("3"), CancellationToken.None);

            Assert.Equal(first, (await billing.NextAsync()).Message.Id);
            Assert.Equal(second, (await billing.NextAsync()).Message.Id);
            Assert.Equal(third, (await billing.NextAsync()).Message.Id);
        });
    }

    [Fact]
    public async Task エンジンの起動より前に発行されたメッセージも届く()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        RecordingSubscriber billing = new("orders", "billing");
        (MessagePublisher publisher, DeliveryEngine engine) = Build(store, [billing]);

        // 合図はエンジンが居ない間に鳴って消える。それでも届くのは、
        // ループが待ちに入る前へ必ず一度取得しに行くから。
        MessageId id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);

        await RunEngineAsync(engine, async () =>
        {
            (Message message, _) = await billing.NextAsync();
            Assert.Equal(id, message.Id);
        });
    }

    [Fact]
    public async Task 停止までに確認できなかった配送は次のエンジンが引き継ぐ()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();

        // 1 台目: 処理が一度も成功しないままエンジンごと止まる（クラッシュの相当）。
        RecordingSubscriber failing = new("orders", "billing", failFirstAttempts: int.MaxValue);
        (MessagePublisher publisher, DeliveryEngine firstEngine) = Build(store, [failing]);

        MessageId id = default;
        await RunEngineAsync(firstEngine, async () =>
        {
            id = await publisher.PublishAsync(Orders, MessagePayload.From("{}"), CancellationToken.None);
            await WaitForAttemptAsync(store, id, failing.Name);
        });

        // 2 台目: 同じ購読名で立て直す。復旧処理は無く、残った Pending を普通に拾うだけ。
        RecordingSubscriber succeeding = new("orders", "billing");
        (_, DeliveryEngine secondEngine) = Build(store, [succeeding]);

        await RunEngineAsync(secondEngine, async () =>
        {
            (Message message, int attempt) = await succeeding.NextAsync();
            Assert.Equal(id, message.Id);
            Assert.True(attempt >= 2, $"再配送なので試行は 2 回以上のはずが {attempt} 回だった。");

            await WaitForStatusAsync(store, id, succeeding.Name, DeliveryStatus.Delivered);
        });
    }

    private (MessagePublisher Publisher, DeliveryEngine Engine) Build(
        SqliteMessageStore store,
        IReadOnlyList<IMessageSubscriber> subscribers)
    {
        SubscriberRegistry registry = new(subscribers);
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

    private static async Task WaitForStatusAsync(
        SqliteMessageStore store,
        MessageId id,
        SubscriptionName subscription,
        DeliveryStatus status)
    {
        using CancellationTokenSource timeout = new(Timeout);
        while (true)
        {
            IReadOnlyList<Delivery> deliveries = await store.GetDeliveriesAsync(id, timeout.Token);
            if (deliveries.Any(d => d.Subscription == subscription && d.Status == status))
            {
                return;
            }

            await Task.Delay(PollInterval, timeout.Token);
        }
    }

    private static async Task WaitForAttemptAsync(
        SqliteMessageStore store,
        MessageId id,
        SubscriptionName subscription)
    {
        using CancellationTokenSource timeout = new(Timeout);
        while (true)
        {
            IReadOnlyList<Delivery> deliveries = await store.GetDeliveriesAsync(id, timeout.Token);
            if (deliveries.Any(d => d.Subscription == subscription && d.AttemptCount > 0))
            {
                return;
            }

            await Task.Delay(PollInterval, timeout.Token);
        }
    }
}
