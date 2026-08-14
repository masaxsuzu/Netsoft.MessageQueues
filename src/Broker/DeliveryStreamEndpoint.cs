using System.Text.Json;

using Netsoft.MessageQueues.Contracts;
using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Runtime;
using Netsoft.MessageQueues.Runtime.Remote;

namespace Netsoft.MessageQueues.Broker;

/// <summary>
/// 配送を流す口（Server-Sent Events）。1 本の接続が 1 レーンを受け持つ。
/// </summary>
/// <remarks>
/// <para>
/// <b>接続が切れたことが、そのまま「処理されなかった」になる。</b>渡した 1 件は
/// <see cref="RemoteConsumer"/> が抱えており、破棄で失敗として配送ループへ返る
/// （<see cref="RemoteSubscriptionHub"/> の注記）。ここが、配送中を永続化せずに
/// at-least-once を保っている要なので、<b>切断を握りつぶして黙って続けてはいけない</b>。
/// </para>
/// <para>
/// 1 接続 = 1 レーンにしてあるのは、レーンの中が直列であることを線の上でも保つため。
/// 1 本の接続に複数レーンを多重化すると、確認の順序と配送の順序を客側で
/// 組み直すことになり、順序の約束をこちらで守れなくなる。並列が欲しい客は
/// レーンの数だけ接続する。
/// </para>
/// </remarks>
internal static class DeliveryStreamEndpoint
{
    /// <summary>
    /// 何も流れないときに送る合図の間隔。
    /// </summary>
    /// <remarks>
    /// 黙って待っていると、客が消えたことに気づけない ── 書き込みが起きて初めて
    /// 切断が分かるので、待っている間は誰も何も書かない。定期的に空の注釈行を送れば、
    /// そこで失敗して消費者が外れ、抱えていた配送が再配送へ回る。
    /// </remarks>
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(MessageQueueRoutes.DeliveryStreamTemplate, StreamAsync);

    private static async Task<IResult> StreamAsync(
        HttpContext http,
        string topic,
        string subscription,
        int lane,
        SubscriberRegistry registry,
        RemoteSubscriptionHub hub,
        CancellationToken cancellationToken)
    {
        if (!RemoteSubscriptionLookup.TryResolve(
                registry, topic, subscription, lane,
                out Topic parsedTopic, out SubscriptionName parsedSubscription, out IResult? failure))
        {
            return failure!;
        }

        RemoteConsumer consumer;
        try
        {
            consumer = hub.Attach(parsedTopic, parsedSubscription, lane);
        }
        catch (RemoteConsumerConflictException exception)
        {
            return TypedResults.Conflict(exception.Message);
        }

        using (consumer)
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";

            // ヘッダを先に流す。ここで流さないと、最初の 1 件が来るまで客は
            // 「繋がった」ことを知れず、繋がってから発行する試験が書けない。
            await http.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                while (true)
                {
                    RemoteDelivery? delivery =
                        await ReadOrKeepAliveAsync(consumer, cancellationToken).ConfigureAwait(false);

                    string frame = delivery is null
                        ? ": keep-alive\n\n"
                        : Frame(delivery);

                    await http.Response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                    await http.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 客が切った、またはホストが止まった。どちらも異常ではない。
                // 抱えたままの 1 件は consumer の破棄が失敗として返す。
            }
        }

        return TypedResults.Empty;
    }

    /// <summary>
    /// 次の 1 件を待つ。<see cref="KeepAlive"/> のあいだ何も来なければ null。
    /// </summary>
    /// <remarks>
    /// <b>読みを放置して <see cref="Task.WhenAny(Task[])"/> で期限を測る形にしてはいけない。</b>
    /// 置き去りにされた読みは後から来た配送を吸い取り、待っている者が居ないまま
    /// その 1 件が宙に浮く（<c>MessageQueueSignal.WaitAsync</c> と同じ話）。
    /// 期限はトークンで伝え、読みそのものを取り消すこと ── 取り消された読みは
    /// 配送を消費しない。
    /// </remarks>
    private static async Task<RemoteDelivery?> ReadOrKeepAliveAsync(
        RemoteConsumer consumer,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource expiry = new(KeepAlive);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);

        try
        {
            return await consumer.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string Frame(RemoteDelivery delivery)
    {
        DeliveryEvent @event = new(
            delivery.Message.Id.Value,
            delivery.Message.Topic.Value,
            delivery.Message.Key.IsEmpty ? null : delivery.Message.Key.Value,
            delivery.Attempt,
            delivery.Message.Payload.Json);

        // data は 1 行。ペイロードは JSON 文字列として包まれているので生の改行を持てず、
        // 枠が壊れない（DeliveryEvent の注記）。
        string json = JsonSerializer.Serialize(@event, MessageQueueJson.Options);
        return $"event: {MessageQueueRoutes.DeliveryEventName}\ndata: {json}\n\n";
    }
}
