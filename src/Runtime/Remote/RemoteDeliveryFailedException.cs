namespace Netsoft.MessageQueues.Runtime.Remote;

/// <summary>
/// 遠隔の消費者へ渡した配送が、確認されずに終わったことを表す。
/// </summary>
/// <remarks>
/// これが投げられる先は <see cref="DeliveryEngine"/> の配送ループで、あそこは
/// <b>購読者が投げた例外と区別しない</b>。区別する必要が無いのが肝で、
/// 「処理が正常に終わらなかったので再配送する」以外の振る舞いはどちらにも要らない。
/// 遠隔だけの再試行や上限をここに足すと、意味論が 2 つに割れる。
/// </remarks>
public sealed class RemoteDeliveryFailedException : Exception
{
    public RemoteDeliveryFailedException(string message)
        : base(message)
    {
    }
}
