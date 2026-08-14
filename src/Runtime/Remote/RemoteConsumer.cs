using System.Threading.Channels;

using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime.Remote;

/// <summary>
/// レーンへ接続した消費者。1 件受け取り、確認するか失敗を返す、を繰り返す。
/// </summary>
/// <remarks>
/// <para>
/// <b>受け取りと確認は別のスレッドから来る。</b>外へ出したときの姿がそうなる ──
/// 配送は開きっぱなしの応答（SSE）に書き、確認は別の要求として届く。
/// だから未確認の 1 件（<c>_outstanding</c>）は錠で守る。
/// </para>
/// <para>
/// <b>破棄は「接続が切れた」と同じ意味。</b>未確認のまま破棄されたら、その 1 件は
/// 失敗として配送ループへ返る（= 再配送される）。ここが、配送中を永続化せずに
/// at-least-once を保っている要（<see cref="RemoteSubscriptionHub"/> の注記）。
/// </para>
/// </remarks>
public sealed class RemoteConsumer : IDisposable
{
    private readonly ChannelReader<RemoteHandover> _pending;
    private readonly Action<RemoteConsumer> _detach;
    private readonly Lock _gate = new();

    private RemoteHandover? _outstanding;
    private bool _disposed;

    internal RemoteConsumer(ChannelReader<RemoteHandover> pending, Action<RemoteConsumer> detach)
    {
        _pending = pending;
        _detach = detach;
    }

    /// <summary>
    /// 次の 1 件を受け取る。無ければ来るまで待つ。
    /// </summary>
    /// <remarks>
    /// 前の 1 件を確認する前に呼んではいけない。呼ぶと未確認の 1 件が忘れられ、
    /// 破棄まで誰も結末を書かなくなる（レーンの中は直列なので、そもそも
    /// 呼ぶ必要が無い）。
    /// </remarks>
    public async Task<RemoteDelivery> ReadAsync(CancellationToken cancellationToken)
    {
        // 取り出しは不可分。取り消しで抜けたときは箱に残るので、次の消費者が拾う。
        RemoteHandover handover = await _pending.ReadAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _outstanding = handover;
        }

        return new RemoteDelivery(handover.Message, handover.Attempt);
    }

    /// <summary>渡された 1 件を確認する。</summary>
    /// <returns>確認できたなら true。未確認の 1 件が無い、または識別子が違うなら false。</returns>
    public bool Ack(MessageId messageId) => Settle(messageId, static handover => handover.TryComplete());

    /// <summary>渡された 1 件を失敗として返す。同じメッセージが再配送される。</summary>
    /// <returns>受け付けたなら true。<see cref="Ack"/> と同じ条件で false。</returns>
    public bool Nack(MessageId messageId, string? reason) =>
        Settle(messageId, handover => handover.TryFail(reason is null
            ? "遠隔の消費者が失敗を返しました。"
            : $"遠隔の消費者が失敗を返しました: {reason}"));

    public void Dispose()
    {
        RemoteHandover? abandoned;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            abandoned = _outstanding;
            _outstanding = null;
        }

        // 錠の外で結末を書く。TrySetException の継続がこの場で走りうるので、
        // 錠を持ったまま呼ぶと配送ループの続きを錠の中へ引き込むことになる。
        abandoned?.TryFail("確認される前に消費者との接続が切れました。");
        _detach(this);
    }

    private bool Settle(MessageId messageId, Func<RemoteHandover, bool> settle)
    {
        RemoteHandover handover;

        lock (_gate)
        {
            if (_outstanding is null || _outstanding.Message.Id != messageId)
            {
                return false;
            }

            handover = _outstanding;
            _outstanding = null;
        }

        return settle(handover);
    }
}
