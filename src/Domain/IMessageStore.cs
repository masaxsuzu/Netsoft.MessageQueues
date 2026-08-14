namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// メッセージと配送の永続化の口。
/// </summary>
/// <remarks>
/// <para>
/// at-least-once を成り立たせているのはこの口の 2 つの約束。
/// (1) <see cref="AppendAsync"/> はメッセージと全購読分の配送行を<b>1 つの取引</b>で書く ──
/// 発行が成功したのに一部の購読にだけ配送行が無い、という中間状態を作らない。
/// (2) <see cref="MarkDeliveredAsync"/> が呼ばれるまで配送行は
/// <see cref="DeliveryStatus.Pending"/> のまま残る ── 確認より先に消える経路が無い。
/// </para>
/// <para>
/// 「配送中」の状態は持たない。理由は <see cref="DeliveryStatus"/> の注記。
/// </para>
/// </remarks>
public interface IMessageStore
{
    /// <summary>
    /// スキーマを作る。プロセス起動時に一度呼ぶ。何度呼んでも安全。
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// メッセージと、指定された購読すべてへの未配送の行を、1 つの取引で永続化する。
    /// </summary>
    /// <exception cref="ArgumentException">
    /// 購読が 1 つも無い場合。誰にも配られないメッセージを永続化しても
    /// 配送の約束を果たす相手が居ない ── 発行側の配線ミスをここで顕在化させる。
    /// </exception>
    Task AppendAsync(
        Message message,
        IReadOnlyCollection<SubscriptionName> subscriptions,
        CancellationToken cancellationToken);

    /// <summary>
    /// 指定された購読・レーンの、最も古い未配送のメッセージを 1 件取得し、試行回数を進める。
    /// 無ければ null。
    /// </summary>
    /// <remarks>
    /// 選択と試行回数の更新は 1 つの取引で行うこと。状態は Pending のまま動かさない
    /// （<see cref="ClaimedDelivery"/> の注記）。レーンの絞り込みは
    /// <see cref="Message.PartitionHash"/> をレーン数で割った余りで行う ── 同じメッセージは
    /// 何度取得しても同じレーンへ落ちる。
    /// </remarks>
    Task<ClaimedDelivery?> TryClaimNextAsync(
        Topic topic,
        SubscriptionName subscription,
        Lane lane,
        CancellationToken cancellationToken);

    /// <summary>
    /// 配送を確認し、<see cref="DeliveryStatus.Delivered"/> にする。
    /// 購読者の処理が正常に終わった<b>後</b>に呼ぶ。
    /// </summary>
    /// <remarks>
    /// 既に Delivered なら何もしない。再配送と確認が重なる経路
    /// （確認の直前に落ちて、再起動後にもう一度処理された）で 2 度呼ばれるのは正常。
    /// </remarks>
    Task MarkDeliveredAsync(
        MessageId messageId,
        SubscriptionName subscription,
        CancellationToken cancellationToken);

    /// <summary>
    /// 指定されたメッセージの配送行をすべて返す。監視とテストの読み口。
    /// </summary>
    Task<IReadOnlyList<Delivery>> GetDeliveriesAsync(
        MessageId messageId,
        CancellationToken cancellationToken);
}
