using System.Text;

using Netsoft.MessageQueues.Contracts;
using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Runtime;

namespace Netsoft.MessageQueues.Broker;

/// <summary>
/// 発行の口。本体がペイロードそのもの。
/// </summary>
/// <remarks>
/// <b>ペイロードを JSON の中へ包まない。</b>包むと、受け取った側で取り出すときに
/// 書式が正規化されうる ── この基盤は「往復で同じバイト列が返る」を約束している
/// （<c>SqliteMessageStore</c> の注記）ので、本体をそのまま値にするのが一番短い道になる。
/// ついでに <c>curl -d @payload.json</c> がそのまま使える。
/// </remarks>
internal static class PublishEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(MessageQueueRoutes.PublishTemplate, PublishAsync);

    private static async Task<IResult> PublishAsync(
        HttpRequest request,
        string topic,
        IMessagePublisher publisher,
        CancellationToken cancellationToken)
    {
        if (!Topic.TryFrom(topic, out Topic parsedTopic))
        {
            return TypedResults.BadRequest("トピック名が空です。");
        }

        PartitionKey key = default;
        string? rawKey = request.Query[MessageQueueRoutes.KeyQueryName];
        if (rawKey is not null && !PartitionKey.TryFrom(rawKey, out key))
        {
            return TypedResults.BadRequest("パーティションキーが空です。付けないなら省いてください。");
        }

        byte[]? body = await ReadCappedAsync(request.Body, MessagePayload.MaxBytes, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!MessagePayload.TryFrom(Encoding.UTF8.GetString(body), out MessagePayload payload))
        {
            return TypedResults.BadRequest(
                $"ペイロードは {MessagePayload.MaxBytes} バイト以下の有効な JSON でなければなりません。");
        }

        try
        {
            MessageId id = await publisher
                .PublishAsync(parsedTopic, payload, key, cancellationToken)
                .ConfigureAwait(false);

            // 201 にしない ── 場所を返せる資源がここには無く、Location が 404 を指すことになる。
            // 202 でもない ── 返った時点で永続化は完了しており、「後でやる」ではない。
            return TypedResults.Ok(new PublishedMessage(id.Value));
        }
        catch (InvalidOperationException exception)
        {
            // 購読の無いトピックへの発行。客の書式は正しいので 400 ではなく、
            // ブローカー側の宣言と噛み合っていないという意味で 409。
            return TypedResults.Conflict(exception.Message);
        }
    }

    /// <summary>
    /// 本体を上限ぶんだけ読む。超えていたら null。
    /// </summary>
    /// <remarks>
    /// 全部読んでから長さを見ない。上限を超えるものを最後まで受け取ってから捨てるのは、
    /// 捨てると決まっているものにメモリと時間を払うことになる。1 バイト余分に読むのは、
    /// 「ちょうど上限」と「超えている」を読み切らずに見分けるため。
    /// </remarks>
    private static async Task<byte[]?> ReadCappedAsync(Stream body, int max, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[max + 1];
        int read = 0;

        while (read < buffer.Length)
        {
            int count = await body.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return read > max ? null : buffer[..read];
    }
}
