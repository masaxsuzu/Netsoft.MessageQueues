namespace Netsoft.MessageQueues.Domain.Tests;

public sealed class LaneTests
{
    [Fact]
    public void 未初期化のレーンは1本構成の先頭として扱える()
    {
        Lane lane = default;

        Assert.Equal(0, lane.Index);
        Assert.Equal(1, lane.Count);
        Assert.Equal(Lane.Single, lane);
    }

    [Fact]
    public void 位置とレーン数からレーンを作れる()
    {
        Lane lane = new(2, 4);

        Assert.Equal(2, lane.Index);
        Assert.Equal(4, lane.Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 4)]
    [InlineData(4, 4)]
    public void レーン数の範囲外は作れない(int index, int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Lane(index, count));
    }

    [Fact]
    public void 同じハッシュは常に同じレーンへ落ちる()
    {
        long hash = PartitionKey.From("order-42").Hash;

        Assert.Equal(Lane.IndexFor(hash, 4), Lane.IndexFor(hash, 4));
        Assert.InRange(Lane.IndexFor(hash, 4), 0, 3);
    }

    [Fact]
    public void Containsは自分のレーンへ落ちるハッシュだけを含む()
    {
        long hash = PartitionKey.From("order-42").Hash;
        int index = Lane.IndexFor(hash, 4);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i == index, new Lane(i, 4).Contains(hash));
        }
    }

    [Fact]
    public void レーンが1本ならすべてのハッシュがそこへ落ちる()
    {
        foreach (int i in Enumerable.Range(0, 100))
        {
            Assert.True(Lane.Single.Contains(PartitionKey.From($"key-{i}").Hash));
        }
    }
}
