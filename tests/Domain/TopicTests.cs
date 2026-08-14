namespace Netsoft.MessageQueues.Domain.Tests;

public sealed class TopicTests
{
    [Fact]
    public void 文字列からトピック名を作れる()
    {
        Topic topic = Topic.From("orders");

        Assert.Equal("orders", topic.Value);
        Assert.False(topic.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void 空文字や空白だけのトピック名は作れない(string value)
    {
        Assert.Throws<ArgumentException>(() => Topic.From(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryFromは不正な値でも例外を投げない(string? value)
    {
        Assert.False(Topic.TryFrom(value, out Topic topic));
        Assert.True(topic.IsEmpty);
    }

    [Fact]
    public void 同じ文字列のトピック名は等しい()
    {
        Assert.Equal(Topic.From("orders"), Topic.From("orders"));
        Assert.NotEqual(Topic.From("orders"), Topic.From("payments"));
    }
}
