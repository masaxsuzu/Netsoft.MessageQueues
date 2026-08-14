using System.Net;
using System.Text;

using Netsoft.MessageQueues.Contracts;
using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Broker.Tests;

public sealed class PublishEndpointTests : IDisposable
{
    private readonly BrokerFactory _broker = new(("orders", "billing", 1));

    public void Dispose() => _broker.Dispose();

    [Fact]
    public async Task 発行すると識別子が返る()
    {
        using HttpClient client = _broker.CreateClient();

        string id = await client.PublishOkAsync("orders", """{"orderId":42}""");

        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task JSONでないペイロードは弾く(string payload)
    {
        using HttpClient client = _broker.CreateClient();

        using HttpResponseMessage response = await client.PublishAsync("orders", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 上限ちょうどのペイロードは受け付ける()
    {
        using HttpClient client = _broker.CreateClient();
        string payload = "\"" + new string('a', MessagePayload.MaxBytes - 2) + "\"";
        Assert.Equal(MessagePayload.MaxBytes, Encoding.UTF8.GetByteCount(payload));

        using HttpResponseMessage response = await client.PublishAsync("orders", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task 上限を超えるペイロードは弾く()
    {
        using HttpClient client = _broker.CreateClient();
        string payload = "\"" + new string('a', MessagePayload.MaxBytes) + "\"";

        using HttpResponseMessage response = await client.PublishAsync("orders", payload);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task 購読の宣言が無いトピックへの発行は受け付けない()
    {
        using HttpClient client = _broker.CreateClient();

        using HttpResponseMessage response = await client.PublishAsync("payments", "{}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task 空のパーティションキーは弾く()
    {
        using HttpClient client = _broker.CreateClient();

        using HttpResponseMessage response = await client.PostAsync(
            $"{MessageQueueRoutes.Publish("orders")}?{MessageQueueRoutes.KeyQueryName}=%20",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
