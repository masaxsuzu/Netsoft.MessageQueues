using System.Net;

using Netsoft.MessageQueues.Contracts;
using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Broker.Tests;

public sealed class DeliveryStreamEndpointTests : IDisposable
{
    private readonly BrokerFactory _broker = new(("orders", "billing", 1), ("orders", "wide", 4));

    public void Dispose() => _broker.Dispose();

    [Fact]
    public async Task 発行したメッセージは繋いだ客に届く()
    {
        using HttpClient client = _broker.CreateClient();
        using DeliveryStreamReader stream = await DeliveryStreamReader.OpenAsync(client, "orders", "billing", 0);

        string id = await client.PublishOkAsync("orders", """{"orderId":42}""");

        DeliveryEvent delivered = await stream.NextAsync();

        Assert.Equal(id, delivered.MessageId);
        Assert.Equal("orders", delivered.Topic);
        Assert.Equal("""{"orderId":42}""", delivered.Payload);
        Assert.Null(delivered.Key);
        Assert.Equal(1, delivered.Attempt);

        // 受け取る側が Message を組み立て直せるだけの項目が載っていること。
        Assert.NotEqual(default, delivered.EnqueuedAt);
    }

    [Fact]
    public async Task 繋ぐ前に発行されたメッセージも繋いだ時点で届く()
    {
        using HttpClient client = _broker.CreateClient();

        string id = await client.PublishOkAsync("orders", "{}");

        using DeliveryStreamReader stream = await DeliveryStreamReader.OpenAsync(client, "orders", "billing", 0);

        Assert.Equal(id, (await stream.NextAsync()).MessageId);
    }

    [Fact]
    public async Task 確認するまで次のメッセージへ進まない()
    {
        using HttpClient client = _broker.CreateClient();
        using DeliveryStreamReader stream = await DeliveryStreamReader.OpenAsync(client, "orders", "billing", 0);

        string first = await client.PublishOkAsync("orders", "1");
        string second = await client.PublishOkAsync("orders", "2");

        Assert.Equal(first, (await stream.NextAsync()).MessageId);

        using (HttpResponseMessage ack = await client.AckAsync("orders", "billing", 0, first))
        {
            Assert.Equal(HttpStatusCode.NoContent, ack.StatusCode);
        }

        Assert.Equal(second, (await stream.NextAsync()).MessageId);
    }

    [Fact]
    public async Task 失敗を申告したメッセージは再配送される()
    {
        using HttpClient client = _broker.CreateClient();
        using DeliveryStreamReader stream = await DeliveryStreamReader.OpenAsync(client, "orders", "billing", 0);

        string id = await client.PublishOkAsync("orders", "{}");
        Assert.Equal(id, (await stream.NextAsync()).MessageId);

        using (HttpResponseMessage nack = await client.NackAsync("orders", "billing", 0, id, "テスト"))
        {
            Assert.Equal(HttpStatusCode.NoContent, nack.StatusCode);
        }

        DeliveryEvent redelivered = await stream.NextAsync();
        Assert.Equal(id, redelivered.MessageId);
        Assert.Equal(2, redelivered.Attempt);
    }

    [Fact]
    public async Task 確認する前に接続が切れたメッセージは再配送される()
    {
        using HttpClient client = _broker.CreateClient();
        string id;

        // 受け取ってから確認せずに切る。客のプロセスが落ちた相当。
        using (DeliveryStreamReader first = await DeliveryStreamReader.OpenAsync(client, "orders", "billing", 0))
        {
            id = await client.PublishOkAsync("orders", "{}");
            Assert.Equal(id, (await first.NextAsync()).MessageId);
        }

        using DeliveryStreamReader second = await DeliveryStreamReader.OpenAsync(client, "orders", "billing", 0);

        DeliveryEvent redelivered = await second.NextAsync();
        Assert.Equal(id, redelivered.MessageId);
        Assert.Equal(2, redelivered.Attempt);
    }

    [Fact]
    public async Task 宣言されていない購読へは繋げない()
    {
        using HttpClient client = _broker.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync(MessageQueueRoutes.DeliveryStream("orders", "unknown", 0));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 範囲外のレーンへは繋げない()
    {
        using HttpClient client = _broker.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync(MessageQueueRoutes.DeliveryStream("orders", "billing", 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 同じレーンへ2本目は繋げない()
    {
        using HttpClient client = _broker.CreateClient();
        using DeliveryStreamReader first = await DeliveryStreamReader.OpenAsync(client, "orders", "billing", 0);

        using HttpResponseMessage second =
            await client.GetAsync(MessageQueueRoutes.DeliveryStream("orders", "billing", 0));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task 渡していない配送への確認は受け付けない()
    {
        using HttpClient client = _broker.CreateClient();
        using DeliveryStreamReader stream = await DeliveryStreamReader.OpenAsync(client, "orders", "billing", 0);

        using HttpResponseMessage response = await client.AckAsync("orders", "billing", 0, "存在しない識別子");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task パーティションキーは配送に載り同じキーは同じレーンへ落ちる()
    {
        using HttpClient client = _broker.CreateClient();
        PartitionKey key = PartitionKey.From("order-42");
        int lane = Lane.IndexFor(key.Hash, 4);

        using DeliveryStreamReader stream = await DeliveryStreamReader.OpenAsync(client, "orders", "wide", lane);

        string first = await client.PublishOkAsync("orders", "1", key.Value);
        string second = await client.PublishOkAsync("orders", "2", key.Value);

        DeliveryEvent one = await stream.NextAsync();
        Assert.Equal(first, one.MessageId);
        Assert.Equal("order-42", one.Key);

        using (HttpResponseMessage ack = await client.AckAsync("orders", "wide", lane, first))
        {
            Assert.Equal(HttpStatusCode.NoContent, ack.StatusCode);
        }

        Assert.Equal(second, (await stream.NextAsync()).MessageId);
    }

    [Fact]
    public async Task 整形済みや多バイト文字のペイロードも同じ文字列で届く()
    {
        using HttpClient client = _broker.CreateClient();
        using DeliveryStreamReader stream = await DeliveryStreamReader.OpenAsync(client, "orders", "billing", 0);

        // 改行を含む整形済みの JSON。SSE の data は行で区切られるので、
        // 包み方を誤るとここで枠が壊れる。
        string payload = "{\n  \"名前\": \"注文\",\n  \"絵文字\": \"🍣\"\n}";

        await client.PublishOkAsync("orders", payload);

        Assert.Equal(payload, (await stream.NextAsync()).Payload);
    }
}
