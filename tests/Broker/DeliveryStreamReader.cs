using System.Net;
using System.Text.Json;

using Netsoft.MessageQueues.Contracts;

namespace Netsoft.MessageQueues.Broker.Tests;

/// <summary>
/// 配送の流れ（SSE）を客の側から読む道具。
/// </summary>
/// <remarks>
/// 破棄は<b>接続を切る</b>意味を持つ。応答を捨てるだけでなく要求そのものを取り消すのは、
/// サーバ側に切断として届くことを確実にするため ── この道具で確かめたいことの 1 つが
/// 「切れたら再配送される」なので、切り方が曖昧だと試験の意味が消える。
/// </remarks>
public sealed class DeliveryStreamReader : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _abort;
    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;

    private DeliveryStreamReader(CancellationTokenSource abort, HttpResponseMessage response, StreamReader reader)
    {
        _abort = abort;
        _response = response;
        _reader = reader;
    }

    public static async Task<DeliveryStreamReader> OpenAsync(
        HttpClient client,
        string topic,
        string subscription,
        int lane)
    {
        ArgumentNullException.ThrowIfNull(client);

        CancellationTokenSource abort = new();
        HttpRequestMessage request = new(
            HttpMethod.Get,
            MessageQueueRoutes.DeliveryStream(topic, subscription, lane));

        HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, abort.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Stream stream = await response.Content.ReadAsStreamAsync(abort.Token);
        return new DeliveryStreamReader(abort, response, new StreamReader(stream));
    }

    /// <summary>次の配送を待つ。合図（keep-alive）は読み飛ばす。</summary>
    public async Task<DeliveryEvent> NextAsync()
    {
        using CancellationTokenSource timeout = new(Timeout);

        while (true)
        {
            string? line = await _reader.ReadLineAsync(timeout.Token);

            if (line is null)
            {
                throw new InvalidOperationException("配送を待っている間に接続が閉じられました。");
            }

            if (line.Length == 0 || line.StartsWith(':'))
            {
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<DeliveryEvent>(line[6..], MessageQueueJson.Options)!;
            }
        }
    }

    public void Dispose()
    {
        _abort.Cancel();
        _reader.Dispose();
        _response.Dispose();
        _abort.Dispose();
    }
}
