using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.E2E.Tests;

/// <summary>
/// ブローカーを別プロセスで起こし、この プロセスから発行して購読する。
/// </summary>
public sealed class CrossProcessTests
{
    private static readonly Topic Orders = Topic.From("orders");

    [Fact]
    public async Task 別プロセスのブローカー越しに発行したメッセージが購読者に届く()
    {
        using TemporaryDatabase database = new();
        await using BrokerProcess broker =
            await BrokerProcess.StartAsync(database.FilePath, ("orders", "billing", 1));
        RecordingSubscriber billing = new("orders", "billing");

        await using ClientHost client = await ClientHost.StartAsync(broker, billing);

        MessageId id = await client.Publisher.PublishAsync(
            Orders, MessagePayload.From("""{"orderId":42}"""), CancellationToken.None);

        (Message message, int attempt) = await billing.NextAsync();

        Assert.Equal(id, message.Id);
        Assert.Equal("""{"orderId":42}""", message.Payload.Json);
        Assert.Equal(1, attempt);

        // 発行時刻も線の上を渡ってくる（捏造していない）。
        Assert.NotEqual(default, message.EnqueuedAt);
    }

    [Fact]
    public async Task 購読者が失敗すると同じメッセージが再配送される()
    {
        using TemporaryDatabase database = new();
        await using BrokerProcess broker =
            await BrokerProcess.StartAsync(database.FilePath, ("orders", "billing", 1));
        RecordingSubscriber billing = new("orders", "billing", failFirstAttempts: 2);

        await using ClientHost client = await ClientHost.StartAsync(broker, billing);

        MessageId id = await client.Publisher.PublishAsync(
            Orders, MessagePayload.From("{}"), CancellationToken.None);

        (Message message, int attempt) = await billing.NextAsync();

        Assert.Equal(id, message.Id);
        Assert.Equal(3, attempt);
    }

    [Fact]
    public async Task パーティションキー付きの発行はキーを保って届く()
    {
        using TemporaryDatabase database = new();
        await using BrokerProcess broker =
            await BrokerProcess.StartAsync(database.FilePath, ("orders", "billing", 1));
        RecordingSubscriber billing = new("orders", "billing");

        await using ClientHost client = await ClientHost.StartAsync(broker, billing);

        PartitionKey key = PartitionKey.From("order-42");
        await client.Publisher.PublishAsync(Orders, MessagePayload.From("{}"), key, CancellationToken.None);

        (Message message, _) = await billing.NextAsync();

        Assert.Equal(key, message.Key);
    }

    [Fact]
    public async Task ブローカーを落として立て直しても未配送は失われない()
    {
        using TemporaryDatabase database = new();
        MessageId id;

        // 誰も繋いでいない間に発行し、そのままブローカーごと落とす。
        await using (BrokerProcess first = await BrokerProcess.StartAsync(database.FilePath, ("orders", "billing", 1)))
        {
            await using ClientHost publisher = await ClientHost.StartAsync(first);
            id = await publisher.Publisher.PublishAsync(
                Orders, MessagePayload.From("""{"orderId":7}"""), CancellationToken.None);

            await first.KillAsync();
        }

        // 同じ DB で立て直す。復旧の手順は無く、未配送がそのまま残っているだけ。
        await using BrokerProcess second = await BrokerProcess.StartAsync(database.FilePath, ("orders", "billing", 1));
        RecordingSubscriber billing = new("orders", "billing");
        await using ClientHost client = await ClientHost.StartAsync(second, billing);

        (Message message, _) = await billing.NextAsync();

        Assert.Equal(id, message.Id);
        Assert.Equal("""{"orderId":7}""", message.Payload.Json);
    }
}
