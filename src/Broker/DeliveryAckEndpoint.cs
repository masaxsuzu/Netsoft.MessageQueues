using Netsoft.MessageQueues.Contracts;
using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Runtime;
using Netsoft.MessageQueues.Runtime.Remote;

namespace Netsoft.MessageQueues.Broker;

/// <summary>
/// 配送の確認と、失敗の申告の口。
/// </summary>
/// <remarks>
/// 配送を流す口（<see cref="DeliveryStreamEndpoint"/>）と別の要求で来る。SSE は
/// サーバから客への一方通行なので、返事は別の口を叩いてもらうしかない。
/// </remarks>
internal static class DeliveryAckEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(MessageQueueRoutes.AckTemplate, Ack);
        endpoints.MapPost(MessageQueueRoutes.NackTemplate, Nack);
    }

    private static IResult Ack(
        string topic,
        string subscription,
        int lane,
        string messageId,
        SubscriberRegistry registry,
        RemoteSubscriptionHub hub) =>
        Settle(topic, subscription, lane, messageId, registry, (t, s, id) => hub.Ack(t, s, lane, id));

    private static IResult Nack(
        string topic,
        string subscription,
        int lane,
        string messageId,
        HttpRequest request,
        SubscriberRegistry registry,
        RemoteSubscriptionHub hub)
    {
        string? reason = request.Query[MessageQueueRoutes.ReasonQueryName];
        return Settle(topic, subscription, lane, messageId, registry, (t, s, id) => hub.Nack(t, s, lane, id, reason));
    }

    private static IResult Settle(
        string topic,
        string subscription,
        int lane,
        string messageId,
        SubscriberRegistry registry,
        Func<Topic, SubscriptionName, MessageId, bool> settle)
    {
        if (!RemoteSubscriptionLookup.TryResolve(
                registry, topic, subscription, lane,
                out Topic parsedTopic, out SubscriptionName parsedSubscription, out IResult? failure))
        {
            return failure!;
        }

        if (!MessageId.TryFrom(messageId, out MessageId parsedMessageId))
        {
            return TypedResults.BadRequest("メッセージの識別子が空です。");
        }

        // 受け付けられないのは「消費者が繋がっていない」か「渡してある配送と識別子が違う」。
        // どちらも客の状態がブローカーの持っているものと食い違っている、という一種類の話。
        return settle(parsedTopic, parsedSubscription, parsedMessageId)
            ? TypedResults.NoContent()
            : TypedResults.Conflict("そのレーンに、確認を待っているその識別子の配送はありません。");
    }
}
