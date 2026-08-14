namespace Netsoft.MessageQueues.Contracts;

/// <summary>
/// ブローカーの口。経路の文字列をブローカーと客の両方がここから取る。
/// </summary>
/// <remarks>
/// <para>
/// <b>トピック名と購読名は経路の一部なので、<c>/</c> を含められない。</b>
/// 含めた場合その購読へは外から繋げない（経路が一致しない）。値そのものは
/// Domain の <c>Topic</c> が受け付けるので、これは HTTP の口だけの制約。
/// クエリ文字列へ移せば外れるが、そうすると「どの購読の話か」が経路から読めなくなる。
/// </para>
/// <para>
/// 版（<c>/v1</c> のような接頭辞）は付けていない。付ける意味が出るのは、互換を保った
/// まま形を変える必要が出たとき ── その時に付ければよく、先回りすると使われない
/// 分岐が増える。
/// </para>
/// </remarks>
public static class MessageQueueRoutes
{
    /// <summary>発行の口（POST）。本体がペイロードそのもの。</summary>
    public const string PublishTemplate = "/topics/{topic}/messages";

    /// <summary>配送を受け取る口（GET、Server-Sent Events）。</summary>
    public const string DeliveryStreamTemplate = "/subscriptions/{topic}/{subscription}/lanes/{lane}/deliveries";

    /// <summary>配送を確認する口（POST）。</summary>
    public const string AckTemplate =
        "/subscriptions/{topic}/{subscription}/lanes/{lane}/deliveries/{messageId}/ack";

    /// <summary>配送を失敗として返す口（POST）。</summary>
    public const string NackTemplate =
        "/subscriptions/{topic}/{subscription}/lanes/{lane}/deliveries/{messageId}/nack";

    /// <summary>パーティションキーを渡すクエリ文字列の名前。</summary>
    public const string KeyQueryName = "key";

    /// <summary>失敗の理由を渡すクエリ文字列の名前。</summary>
    public const string ReasonQueryName = "reason";

    /// <summary>SSE の event 名。</summary>
    public const string DeliveryEventName = "delivery";

    public static string Publish(string topic, string? key = null)
    {
        string path = $"/topics/{Uri.EscapeDataString(topic)}/messages";
        return key is null ? path : $"{path}?{KeyQueryName}={Uri.EscapeDataString(key)}";
    }

    public static string DeliveryStream(string topic, string subscription, int lane) =>
        $"/subscriptions/{Uri.EscapeDataString(topic)}/{Uri.EscapeDataString(subscription)}/lanes/{lane}/deliveries";

    public static string Ack(string topic, string subscription, int lane, string messageId) =>
        $"{DeliveryStream(topic, subscription, lane)}/{Uri.EscapeDataString(messageId)}/ack";

    public static string Nack(string topic, string subscription, int lane, string messageId, string? reason = null)
    {
        string path = $"{DeliveryStream(topic, subscription, lane)}/{Uri.EscapeDataString(messageId)}/nack";
        return reason is null ? path : $"{path}?{ReasonQueryName}={Uri.EscapeDataString(reason)}";
    }
}
