using System.Collections.Concurrent;
using System.Threading.Channels;

using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime;

/// <summary>
/// 「配れるメッセージが増えたかもしれない」という合図。発行側が <see cref="Set"/> で鳴らし、
/// 配送エンジンが <see cref="WaitAsync"/> で待つ。アイドルポーリングの置き換え。
/// </summary>
/// <remarks>
/// <para>
/// 箱は購読ごとに 1 つ。合図は購読を名指しで鳴らすので、待ち手どうしが
/// 1 つの箱を取り合うことが無い。容量は 1 ── 各購読の待ち手は配送ループの
/// 1 本だけ（<see cref="DeliveryEngine"/>）で、合図は「仕事があるかもしれない」以上の
/// 情報を持たないから、同じ待ち手を 2 度起こす分は捨ててよい。
/// あふれた分は捨てる（<see cref="BoundedChannelFullMode.DropWrite"/>）。
/// 捨ててもメッセージは取り残されない ── 起きたループは未配送が尽きるまで回り続けるので、
/// 捨てた合図が指していたメッセージも同じ周回で拾われる。
/// </para>
/// <para>
/// プロセス内の Channel は best-effort ではない。<see cref="Set"/> が
/// <see cref="WaitAsync"/> の開始より先でもトークンが箱に残るため、後から始めた待ちは
/// 即座に返る。「確認してから待ちに入るまでの間に発行された」を取りこぼす窓が無い。
/// エンジンが安全網のポーリング無しで合図だけに頼れるのはこの性質による
/// （Netsoft.Jobs の JobQueueSignal と同じ判断）。
/// </para>
/// </remarks>
public sealed class MessageQueueSignal
{
    private readonly ConcurrentDictionary<(string Topic, string Subscription), Channel<byte>> _signals = new();

    /// <summary>
    /// 指定された購読への合図を鳴らす。箱が満杯（1 つ）なら何もしない。
    /// </summary>
    /// <remarks>
    /// TryWrite は満杯でも待たずに false を返すだけ。発火元は発行の経路なので、
    /// エンジンの消費を発行が待つ形にしてはいけない。
    /// </remarks>
    public void Set(Topic topic, SubscriptionName subscription) =>
        ChannelFor(topic, subscription).Writer.TryWrite(0);

    /// <summary>
    /// 指定された購読への合図が鳴るまで待ち、1 つ消費する。既に鳴っていれば即座に返る。
    /// </summary>
    public async Task WaitAsync(Topic topic, SubscriptionName subscription, CancellationToken cancellationToken)
    {
        // 値は見ない。合図は「発行があった」以上の情報を運ばない契約。
        _ = await ChannelFor(topic, subscription).Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private Channel<byte> ChannelFor(Topic topic, SubscriptionName subscription) =>
        _signals.GetOrAdd((topic.Value, subscription.Value), static _ =>
            Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
            }));
}
