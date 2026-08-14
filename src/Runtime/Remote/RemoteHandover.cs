using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime.Remote;

/// <summary>
/// 配送ループが消費者へ渡す 1 件と、その結末を伝える約束。
/// </summary>
/// <remarks>
/// 結末は 3 つ（確認・失敗・接続断）だが、<b>失敗と接続断は同じ扱い</b>にしてある。
/// どちらも「処理が正常に終わらなかった」で、配送ループがすることは変わらない。
/// </remarks>
internal sealed class RemoteHandover
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal RemoteHandover(Message message, int attempt)
    {
        Message = message;
        Attempt = attempt;
    }

    internal Message Message { get; }

    internal int Attempt { get; }

    /// <summary>確認されたら完了し、失敗・接続断なら例外になる。</summary>
    internal Task Completion => _completion.Task;

    /// <remarks>
    /// TrySet を使うのは、既に決着したものへの二度目が普通に起こるから ──
    /// 確認の直後に接続が切れれば、破棄の側も結末を書きに来る。先に書いた方が勝つ。
    /// </remarks>
    internal bool TryComplete() => _completion.TrySetResult();

    internal bool TryFail(string reason) =>
        _completion.TrySetException(new RemoteDeliveryFailedException(reason));
}
