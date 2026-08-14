namespace Netsoft.MessageQueues.Domain.Tests;

public sealed class MessageTests
{
    private static readonly DateTimeOffset EnqueuedAt = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 識別子とトピックとペイロードからメッセージを作れる()
    {
        Message message = new(
            MessageId.From("msg-1"),
            Topic.From("orders"),
            MessagePayload.From("""{"orderId":42}"""),
            EnqueuedAt);

        Assert.Equal("msg-1", message.Id.Value);
        Assert.Equal("orders", message.Topic.Value);
        Assert.Equal("""{"orderId":42}""", message.Payload.Json);
        Assert.Equal(EnqueuedAt, message.EnqueuedAt);
    }

    [Fact]
    public void 識別子が空のメッセージは作れない()
    {
        Assert.Throws<ArgumentException>(() => new Message(
            default,
            Topic.From("orders"),
            MessagePayload.From("{}"),
            EnqueuedAt));
    }

    [Fact]
    public void トピックが空のメッセージは作れない()
    {
        Assert.Throws<ArgumentException>(() => new Message(
            MessageId.From("msg-1"),
            default,
            MessagePayload.From("{}"),
            EnqueuedAt));
    }

    [Fact]
    public void キー付きのメッセージはキーでレーンのハッシュが決まる()
    {
        PartitionKey key = PartitionKey.From("order-42");
        Message first = new(
            MessageId.From("msg-1"), Topic.From("orders"), MessagePayload.From("{}"), EnqueuedAt, key);
        Message second = new(
            MessageId.From("msg-2"), Topic.From("orders"), MessagePayload.From("{}"), EnqueuedAt, key);

        // 識別子が違ってもキーが同じなら同じハッシュ ── 同じレーンへ落ちて順序が守られる根拠。
        Assert.Equal(first.PartitionHash, second.PartitionHash);
        Assert.Equal(key.Hash, first.PartitionHash);
    }

    [Fact]
    public void キーの無いメッセージは識別子でレーンのハッシュが決まる()
    {
        Message message = new(
            MessageId.From("msg-1"), Topic.From("orders"), MessagePayload.From("{}"), EnqueuedAt);

        Assert.True(message.Key.IsEmpty);
        Assert.True(message.PartitionHash >= 0);

        Message same = new(
            MessageId.From("msg-1"), Topic.From("orders"), MessagePayload.From("{}"), EnqueuedAt);
        Assert.Equal(message.PartitionHash, same.PartitionHash);
    }

    [Fact]
    public void ペイロードが空のメッセージは作れない()
    {
        Assert.Throws<ArgumentException>(() => new Message(
            MessageId.From("msg-1"),
            Topic.From("orders"),
            default,
            EnqueuedAt));
    }
}
