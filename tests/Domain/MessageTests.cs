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
    public void ペイロードが空のメッセージは作れない()
    {
        Assert.Throws<ArgumentException>(() => new Message(
            MessageId.From("msg-1"),
            Topic.From("orders"),
            default,
            EnqueuedAt));
    }
}
