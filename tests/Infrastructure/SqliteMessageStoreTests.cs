using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Infrastructure.Tests;

public sealed class SqliteMessageStoreTests : IDisposable
{
    private static readonly Topic Orders = Topic.From("orders");
    private static readonly SubscriptionName Billing = SubscriptionName.From("billing");
    private static readonly SubscriptionName Audit = SubscriptionName.From("audit");

    private readonly TemporaryDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task 発行は全購読分の未配送の行を作る()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        Message message = NewMessage("msg-1");

        await store.AppendAsync(message, [Billing, Audit], CancellationToken.None);

        IReadOnlyList<Delivery> deliveries = await store.GetDeliveriesAsync(message.Id, CancellationToken.None);
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, d => Assert.Equal(DeliveryStatus.Pending, d.Status));
        Assert.All(deliveries, d => Assert.Equal(0, d.AttemptCount));
        Assert.Equal([Audit, Billing], deliveries.Select(d => d.Subscription));
    }

    [Fact]
    public async Task 購読の無い発行は弾く()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AppendAsync(NewMessage("msg-1"), [], CancellationToken.None));
    }

    [Fact]
    public async Task 取得は最も古い未配送を返し試行回数を進める()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        Message first = NewMessage("msg-1");
        Message second = NewMessage("msg-2");
        await store.AppendAsync(first, [Billing], CancellationToken.None);
        await store.AppendAsync(second, [Billing], CancellationToken.None);

        ClaimedDelivery? claimed = await store.TryClaimNextAsync(Orders, Billing, CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(first.Id, claimed.Message.Id);
        Assert.Equal(1, claimed.Attempt);
        Assert.Equal(first.Payload, claimed.Message.Payload);
        Assert.Equal(first.EnqueuedAt, claimed.Message.EnqueuedAt);
    }

    [Fact]
    public async Task 確認するまで同じメッセージが返り続ける()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        Message message = NewMessage("msg-1");
        await store.AppendAsync(message, [Billing], CancellationToken.None);

        ClaimedDelivery? first = await store.TryClaimNextAsync(Orders, Billing, CancellationToken.None);
        ClaimedDelivery? second = await store.TryClaimNextAsync(Orders, Billing, CancellationToken.None);

        Assert.Equal(message.Id, first!.Message.Id);
        Assert.Equal(message.Id, second!.Message.Id);
        Assert.Equal(1, first.Attempt);
        Assert.Equal(2, second.Attempt);
    }

    [Fact]
    public async Task 未配送が無ければ取得はnullを返す()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();

        Assert.Null(await store.TryClaimNextAsync(Orders, Billing, CancellationToken.None));
    }

    [Fact]
    public async Task 別のトピックや別の購読の行は取得できない()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        Message message = NewMessage("msg-1");
        await store.AppendAsync(message, [Billing], CancellationToken.None);

        Assert.Null(await store.TryClaimNextAsync(Topic.From("payments"), Billing, CancellationToken.None));
        Assert.Null(await store.TryClaimNextAsync(Orders, Audit, CancellationToken.None));
    }

    [Fact]
    public async Task 確認した配送は取得の対象から外れる()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        Message message = NewMessage("msg-1");
        await store.AppendAsync(message, [Billing], CancellationToken.None);

        await store.MarkDeliveredAsync(message.Id, Billing, CancellationToken.None);

        Assert.Null(await store.TryClaimNextAsync(Orders, Billing, CancellationToken.None));
        IReadOnlyList<Delivery> deliveries = await store.GetDeliveriesAsync(message.Id, CancellationToken.None);
        Assert.Equal(DeliveryStatus.Delivered, Assert.Single(deliveries).Status);
    }

    [Fact]
    public async Task 片方の購読を確認してももう片方は未配送のまま残る()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        Message message = NewMessage("msg-1");
        await store.AppendAsync(message, [Billing, Audit], CancellationToken.None);

        await store.MarkDeliveredAsync(message.Id, Billing, CancellationToken.None);

        Assert.Null(await store.TryClaimNextAsync(Orders, Billing, CancellationToken.None));
        ClaimedDelivery? claimed = await store.TryClaimNextAsync(Orders, Audit, CancellationToken.None);
        Assert.Equal(message.Id, claimed!.Message.Id);
    }

    [Fact]
    public async Task 確認は2度呼ばれても結果が変わらない()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        Message message = NewMessage("msg-1");
        await store.AppendAsync(message, [Billing], CancellationToken.None);

        await store.MarkDeliveredAsync(message.Id, Billing, CancellationToken.None);
        await store.MarkDeliveredAsync(message.Id, Billing, CancellationToken.None);

        IReadOnlyList<Delivery> deliveries = await store.GetDeliveriesAsync(message.Id, CancellationToken.None);
        Assert.Equal(DeliveryStatus.Delivered, Assert.Single(deliveries).Status);
    }

    [Fact]
    public async Task 未配送のメッセージはストアを開き直しても残る()
    {
        SqliteMessageStore first = await _database.OpenStoreAsync();
        Message message = NewMessage("msg-1");
        await first.AppendAsync(message, [Billing], CancellationToken.None);

        // 同じファイルを別インスタンスで開き直す。プロセス再起動の相当。
        SqliteMessageStore second = await _database.OpenStoreAsync();

        ClaimedDelivery? claimed = await second.TryClaimNextAsync(Orders, Billing, CancellationToken.None);
        Assert.Equal(message.Id, claimed!.Message.Id);
        Assert.Equal(message.Payload, claimed.Message.Payload);
    }

    [Fact]
    public async Task 多バイト文字を含むペイロードは往復しても同じ文字列になる()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        string json = """{"名前":"注文","絵文字":"🍣"}""";
        Message message = new(
            MessageId.From("msg-1"),
            Orders,
            MessagePayload.From(json),
            new DateTimeOffset(2026, 8, 14, 1, 2, 3, TimeSpan.Zero));
        await store.AppendAsync(message, [Billing], CancellationToken.None);

        ClaimedDelivery? claimed = await store.TryClaimNextAsync(Orders, Billing, CancellationToken.None);

        Assert.Equal(json, claimed!.Message.Payload.Json);
    }

    [Fact]
    public async Task 発行時刻はオフセット付きでも同じ瞬間として往復する()
    {
        SqliteMessageStore store = await _database.OpenStoreAsync();
        DateTimeOffset tokyo = new(2026, 8, 14, 9, 0, 0, TimeSpan.FromHours(9));
        Message message = new(MessageId.From("msg-1"), Orders, MessagePayload.From("{}"), tokyo);
        await store.AppendAsync(message, [Billing], CancellationToken.None);

        ClaimedDelivery? claimed = await store.TryClaimNextAsync(Orders, Billing, CancellationToken.None);

        Assert.Equal(tokyo, claimed!.Message.EnqueuedAt);
    }

    private static Message NewMessage(string id) => new(
        MessageId.From(id),
        Orders,
        MessagePayload.From($$"""{"id":"{{id}}"}"""),
        new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero));
}
