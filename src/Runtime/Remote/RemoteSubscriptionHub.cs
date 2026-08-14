using System.Collections.Concurrent;
using System.Threading.Channels;

using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime.Remote;

/// <summary>
/// 配送ループと、プロセス外の消費者との待ち合わせ場所。レーンごとに 1 組。
/// </summary>
/// <remarks>
/// <para>
/// <b>ここに至っても「配送中」は永続化しない。</b>渡した 1 件はこのオブジェクト
/// （プロセスのメモリ）にしか無く、ブローカーが死ねば一緒に消えて、ディスクには
/// <see cref="DeliveryStatus.Pending"/> だけが残る ── 起動時の復旧が要らないという
/// 設計線（<see cref="DeliveryStatus"/> の注記）が、消費者が外へ出ても変わらない。
/// 成り立つのは<b>接続が切れたことをブローカーが観測できる</b>から。
/// 共有 DB を複数プロセスが直接開く形では観測できず、期限つきの所有権を
/// 永続化するしかなくなる（docs/operating.md「複数プロセスにしたくなったら」）。
/// </para>
/// <para>
/// 待ち合わせは<b>レーン</b>単位で、1 レーンに消費者は 1 つ。レーンの中が直列で
/// あることは順序の約束そのものなので、ここを緩めると遠隔購読だけ順序が消える。
/// </para>
/// </remarks>
public sealed class RemoteSubscriptionHub
{
    private readonly ConcurrentDictionary<(string Topic, string Subscription, int Lane), RemoteLane> _lanes = new();

    /// <summary>
    /// レーンへ消費者として接続する。返ったものを破棄すると接続が切れる。
    /// </summary>
    /// <exception cref="RemoteConsumerConflictException">既に消費者が接続している場合。</exception>
    public RemoteConsumer Attach(Topic topic, SubscriptionName subscription, int laneIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(laneIndex);

        RemoteLane lane = LaneFor(topic, subscription, laneIndex);
        return lane.Attach(topic, subscription, laneIndex);
    }

    /// <summary>
    /// 渡した配送を確認する。処理が正常に終わった<b>後</b>に呼ぶ。
    /// </summary>
    /// <returns>
    /// 確認できたなら true。消費者が接続していない、または渡してある配送と
    /// 識別子が食い違う場合は false（呼び出し側はそれを拒否として外へ返す）。
    /// </returns>
    public bool Ack(Topic topic, SubscriptionName subscription, int laneIndex, MessageId messageId) =>
        LaneFor(topic, subscription, laneIndex).Consumer?.Ack(messageId) ?? false;

    /// <summary>
    /// 渡した配送を失敗として返す。同じメッセージが再配送される。
    /// </summary>
    /// <returns>受け付けたなら true。<see cref="Ack"/> と同じ条件で false。</returns>
    public bool Nack(Topic topic, SubscriptionName subscription, int laneIndex, MessageId messageId, string? reason) =>
        LaneFor(topic, subscription, laneIndex).Consumer?.Nack(messageId, reason) ?? false;

    /// <summary>
    /// 1 件を消費者へ渡し、確認されるまで待つ。<see cref="RemoteSubscriber"/> から呼ばれる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>消費者が居なくても待つ。例外にしない。</b>購読は接続とは無関係に存在し続ける
    /// （<see cref="SubscriptionName"/> の注記）ので、繋がっていないのは異常ではなく
    /// 「まだ取りに来ていない」だけ。例外にすると、誰も居ないレーンが
    /// <see cref="MessageQueueOptions.RetryDelay"/> ごとに警告を吐き続ける。
    /// </para>
    /// <para>
    /// 待っている間その 1 件は掴んだまま（試行回数は既に進んでいる）になるが、
    /// 状態は Pending のままなので、ブローカーが死んでも失われない。
    /// </para>
    /// </remarks>
    internal async Task DeliverAsync(
        Topic topic,
        SubscriptionName subscription,
        int laneIndex,
        Message message,
        int attempt,
        CancellationToken cancellationToken)
    {
        RemoteLane lane = LaneFor(topic, subscription, laneIndex);
        RemoteHandover handover = new(message, attempt);

        await lane.Pending.Writer.WriteAsync(handover, cancellationToken).ConfigureAwait(false);

        // 停止で抜けるとき、置いた 1 件は箱に残るか消費者が抱えたままになる。どちらも
        // 確認は書かれていないので次の起動で再配送される ── 取り消しの後始末は要らない。
        await handover.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private RemoteLane LaneFor(Topic topic, SubscriptionName subscription, int laneIndex) =>
        _lanes.GetOrAdd((topic.Value, subscription.Value, laneIndex), static _ => new RemoteLane());

    /// <summary>1 レーンぶんの待ち合わせ。</summary>
    private sealed class RemoteLane
    {
        // 容量 1。レーンの配送ループは 1 本で、確認を待ってから次を取りに行くので、
        // 渡したい 1 件が 2 つ同時に並ぶことはない。
        internal Channel<RemoteHandover> Pending { get; } =
            Channel.CreateBounded<RemoteHandover>(1);

        internal RemoteConsumer? Consumer { get; private set; }

        private readonly Lock _gate = new();

        internal RemoteConsumer Attach(Topic topic, SubscriptionName subscription, int laneIndex)
        {
            lock (_gate)
            {
                if (Consumer is not null)
                {
                    throw new RemoteConsumerConflictException(
                        $"購読 ({topic}, {subscription}) のレーン {laneIndex} には既に消費者が接続しています。");
                }

                RemoteConsumer consumer = new(Pending.Reader, Detach);
                Consumer = consumer;
                return consumer;
            }
        }

        private void Detach(RemoteConsumer consumer)
        {
            lock (_gate)
            {
                // 自分が今の消費者のときだけ外す。取り違えると、後から接続した者を
                // 前任者の破棄が蹴り出すことになる。
                if (ReferenceEquals(Consumer, consumer))
                {
                    Consumer = null;
                }
            }
        }
    }
}
