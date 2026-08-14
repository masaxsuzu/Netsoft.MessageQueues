namespace Netsoft.MessageQueues.Domain.Tests;

public sealed class SubscriptionNameTests
{
    [Fact]
    public void 文字列から購読名を作れる()
    {
        SubscriptionName name = SubscriptionName.From("billing");

        Assert.Equal("billing", name.Value);
        Assert.False(name.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void 空文字や空白だけの購読名は作れない(string value)
    {
        Assert.Throws<ArgumentException>(() => SubscriptionName.From(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryFromは不正な値でも例外を投げない(string? value)
    {
        Assert.False(SubscriptionName.TryFrom(value, out SubscriptionName name));
        Assert.True(name.IsEmpty);
    }

    [Fact]
    public void 同じ文字列の購読名は等しい()
    {
        Assert.Equal(SubscriptionName.From("billing"), SubscriptionName.From("billing"));
        Assert.NotEqual(SubscriptionName.From("billing"), SubscriptionName.From("audit"));
    }
}
