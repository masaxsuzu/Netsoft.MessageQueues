using System.Text.Json;

using Netsoft.MessageQueues.Contracts;

namespace Netsoft.MessageQueues.Client;

/// <summary>
/// 配送の流れ（SSE）を 1 件ずつ読む。
/// </summary>
/// <remarks>
/// 汎用の SSE 実装にしない。この基盤が流すのは 1 種類の event だけで、data は必ず
/// 1 行に収まる（<see cref="DeliveryEvent"/> の注記）。仕様の全体
/// （複数行 data・id・retry・再接続の Last-Event-ID）を実装すると、使われない枝が
/// 大半になる。**足りなくなったらそのとき足す。**
/// </remarks>
internal sealed class DeliveryStream : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;

    private DeliveryStream(HttpResponseMessage response, StreamReader reader)
    {
        _response = response;
        _reader = reader;
    }

    internal static async Task<DeliveryStream> OpenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new DeliveryStream(response, new StreamReader(stream));
    }

    /// <summary>
    /// 次の配送を待つ。接続が閉じられたら null。
    /// </summary>
    /// <remarks>
    /// 合図（<c>:</c> で始まる注釈行）は読み飛ばす。あれはブローカーが
    /// 「客がまだ居るか」を確かめるために書いているだけで、配送ではない。
    /// </remarks>
    internal async Task<DeliveryEvent?> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                return null;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<DeliveryEvent>(line[6..], MessageQueueJson.Options);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        _response.Dispose();
        await ValueTask.CompletedTask;
    }
}
