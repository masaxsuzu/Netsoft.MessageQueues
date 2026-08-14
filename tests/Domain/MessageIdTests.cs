namespace Netsoft.MessageQueues.Domain.Tests;

public sealed class MessageIdTests
{
    [Fact]
    public void 文字列から識別子を作れる()
    {
        MessageId id = MessageId.From("msg-1");

        Assert.Equal("msg-1", id.Value);
        Assert.Equal("msg-1", id.ToString());
        Assert.False(id.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void 空文字や空白だけの識別子は作れない(string value)
    {
        Assert.Throws<ArgumentException>(() => MessageId.From(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryFromは不正な値でも例外を投げない(string? value)
    {
        Assert.False(MessageId.TryFrom(value, out MessageId id));
        Assert.True(id.IsEmpty);
    }

    [Fact]
    public void 同じ文字列の識別子は等しい()
    {
        Assert.Equal(MessageId.From("msg-1"), MessageId.From("msg-1"));
        Assert.NotEqual(MessageId.From("msg-1"), MessageId.From("msg-2"));
    }

    [Fact]
    public void 未初期化の識別子は空として扱える()
    {
        MessageId id = default;

        Assert.True(id.IsEmpty);
        Assert.Equal(string.Empty, id.Value);
    }
}
