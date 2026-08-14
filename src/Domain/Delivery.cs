namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// 1 つのメッセージの、1 つの購読への配送。メッセージと購読の組ごとに 1 行。
/// </summary>
/// <remarks>
/// 複数購読はここで実現される。発行の時点で購読の数だけ行が作られ
/// (<see cref="IMessageStore.AppendAsync"/>)、それぞれが独立に
/// <see cref="DeliveryStatus.Delivered"/> へ進む。片方の購読者が遅くても
/// もう片方は先へ進めるし、どちらが未配送かはこの行を見れば分かる。
/// </remarks>
/// <param name="MessageId">配送するメッセージの識別子。</param>
/// <param name="Subscription">配送先の購読。</param>
/// <param name="Status">配送の状態。</param>
/// <param name="AttemptCount">配送を試みた回数。0 なら一度も渡していない。</param>
public sealed record Delivery(
    MessageId MessageId,
    SubscriptionName Subscription,
    DeliveryStatus Status,
    int AttemptCount);
