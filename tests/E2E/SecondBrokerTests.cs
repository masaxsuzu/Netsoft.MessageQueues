using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.E2E.Tests;

/// <summary>
/// 同じ DB を使うブローカーが 1 つに限られていること。
/// </summary>
/// <remarks>
/// <b>2 本 1 組で意味を持つ。</b>「2 つ目は落ちる」だけなら、終了時に消す印で排他する
/// 実装でも通ってしまう ── その実装は強制終了で印を残し、**次の正しい起動を永久に拒む**。
/// 「殺した後なら起動できる」を並べて初めて、錠を OS に握らせていることが固定される
/// （<c>src/Broker/BrokerLock.cs</c>）。
/// </remarks>
public sealed class SecondBrokerTests
{
    [Fact]
    public async Task 同じDBを使う2つ目のブローカーは立ち上がらない()
    {
        using TemporaryDatabase database = new();
        await using BrokerProcess first =
            await BrokerProcess.StartAsync(database.FilePath, ("orders", "billing", 1));

        string output = await BrokerProcess.StartExpectingFailureAsync(
            database.FilePath, ("orders", "billing", 1));

        // 落ちた理由が錠であること。ポートの取り合いや設定の誤りで落ちたのでは意味が無い。
        Assert.Contains("ブローカーが既に動いています", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 強制終了の後なら同じDBで起動できる()
    {
        using TemporaryDatabase database = new();

        await using (BrokerProcess first =
            await BrokerProcess.StartAsync(database.FilePath, ("orders", "billing", 1)))
        {
            // 正常な停止ではなく殺す。錠を離す後始末は走らない。
            await first.KillAsync();
        }

        await using BrokerProcess second =
            await BrokerProcess.StartAsync(database.FilePath, ("orders", "billing", 1));

        // 立ち上がるだけでなく、実際に受け付けられること。
        RecordingSubscriber billing = new("orders", "billing");
        await using ClientHost client = await ClientHost.StartAsync(second, billing);

        MessageId id = await client.Publisher.PublishAsync(
            Topic.From("orders"), MessagePayload.From("{}"), CancellationToken.None);

        Assert.Equal(id, (await billing.NextAsync()).Message.Id);
    }

    [Fact]
    public async Task DBのパスを分ければ同時に立てられる()
    {
        using TemporaryDatabase first = new();
        using TemporaryDatabase second = new();

        await using BrokerProcess one = await BrokerProcess.StartAsync(first.FilePath, ("orders", "billing", 1));
        await using BrokerProcess two = await BrokerProcess.StartAsync(second.FilePath, ("orders", "billing", 1));
    }
}
