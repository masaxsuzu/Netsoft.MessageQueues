namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// 配送エンジンが取得した、これから購読者へ渡す 1 件。
/// </summary>
/// <remarks>
/// 取得（<see cref="IMessageStore.TryClaimNextAsync"/>）は試行回数を進めるが、
/// 状態は <see cref="DeliveryStatus.Pending"/> のまま動かさない。
/// だからこの型は「配送中」という永続状態の代わりであり、プロセスが消えれば一緒に消える。
/// 残った Pending の行が再配送を保証する（<see cref="DeliveryStatus"/> の注記）。
/// </remarks>
/// <param name="Message">配送するメッセージ本体。</param>
/// <param name="Subscription">配送先の購読。</param>
/// <param name="Attempt">今回が何度目の試行か（1 始まり）。</param>
public sealed record ClaimedDelivery(
    Message Message,
    SubscriptionName Subscription,
    int Attempt);
