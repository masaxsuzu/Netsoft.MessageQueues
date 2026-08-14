using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime.Remote;

/// <summary>
/// プロセス外で処理される購読。配送ループから見ればただの
/// <see cref="IMessageSubscriber"/> で、処理の中身が待ち合わせ場所への受け渡しになっている。
/// </summary>
/// <remarks>
/// <para>
/// <b>遠隔配送のためのループを別に立てない。</b>ここを購読者の 1 つに見せることで、
/// 取得・順序・再試行・確認の実装が <see cref="DeliveryEngine"/> の 1 か所に残る。
/// 2 本目の配送経路を作ると、at-least-once の要である「確認は処理の後にしか
/// 書かれない」を 2 か所で守ることになる。
/// </para>
/// <para>
/// 購読の宣言（トピック・名前・レーン数）はブローカーの起動時に決まり、接続では増えない。
/// 配送行は発行の瞬間に居た購読へ向けて作られる（<see cref="IMessageStore.AppendAsync"/>）ので、
/// 接続で購読が生まれる作りにすると、繋ぐまでに発行されたぶんが誰にも配られない
/// ── 「購読は接続とは無関係に存在し続ける」を保つほうを選んでいる。
/// </para>
/// </remarks>
public sealed class RemoteSubscriber : IMessageSubscriber
{
    private readonly RemoteSubscriptionHub _hub;

    public RemoteSubscriber(Topic topic, SubscriptionName name, int lanes, RemoteSubscriptionHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);

        Topic = topic;
        Name = name;
        Lanes = lanes;
        _hub = hub;
    }

    public Topic Topic { get; }

    public SubscriptionName Name { get; }

    public int Lanes { get; }

    /// <inheritdoc />
    /// <remarks>
    /// レーンは<b>メッセージから計算する</b>。エンジンはループごとのレーンを
    /// 引数で渡さないが、割り当ての規則（<see cref="Lane.IndexFor"/>）は共有されているので、
    /// 同じメッセージからは必ず同じレーンが出る。エンジンの契約を増やさずに済む。
    /// </remarks>
    public Task HandleAsync(Message message, int attempt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return _hub.DeliverAsync(
            Topic,
            Name,
            Lane.IndexFor(message.PartitionHash, Lanes),
            message,
            attempt,
            cancellationToken);
    }
}
