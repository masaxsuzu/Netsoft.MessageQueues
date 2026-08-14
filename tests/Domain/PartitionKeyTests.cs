namespace Netsoft.MessageQueues.Domain.Tests;

public sealed class PartitionKeyTests
{
    [Fact]
    public void 文字列からキーを作れる()
    {
        PartitionKey key = PartitionKey.From("order-42");

        Assert.Equal("order-42", key.Value);
        Assert.False(key.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void 空文字や空白だけのキーは作れない(string value)
    {
        Assert.Throws<ArgumentException>(() => PartitionKey.From(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryFromは不正な値でも例外を投げない(string? value)
    {
        Assert.False(PartitionKey.TryFrom(value, out PartitionKey key));
        Assert.True(key.IsEmpty);
    }

    [Fact]
    public void 同じ文字列のキーは同じハッシュを持つ()
    {
        // レーンの割り当てはこの性質の上に立つ。別インスタンスでも値が同じなら同じレーン。
        Assert.Equal(PartitionKey.From("order-42").Hash, PartitionKey.From("order-42").Hash);
    }

    [Fact]
    public void 異なる文字列のキーは異なるハッシュを持つ()
    {
        Assert.NotEqual(PartitionKey.From("order-42").Hash, PartitionKey.From("order-43").Hash);
    }

    [Fact]
    public void ハッシュは負にならない()
    {
        // SQLite の % が負の余りを返す経路を塞ぐ（StableHash の注記）。
        foreach (int i in Enumerable.Range(0, 1000))
        {
            Assert.True(PartitionKey.From($"key-{i}").Hash >= 0);
        }
    }

    [Fact]
    public void 未初期化のキーは空として扱える()
    {
        PartitionKey key = default;

        Assert.True(key.IsEmpty);
        Assert.Equal(string.Empty, key.Value);
    }
}
