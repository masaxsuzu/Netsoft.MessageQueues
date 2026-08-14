namespace Netsoft.MessageQueues.Runtime.Remote;

/// <summary>
/// 既に消費者が接続しているレーンへ、2 つ目が接続しようとしたことを表す。
/// </summary>
/// <remarks>
/// 待たせずに弾く。レーンの中が直列であることは順序の約束そのもの
/// （docs/operating.md）なので、2 つ目を並べたらその購読の順序が消える。
/// 待たせる作りにもしない ── 待てるということは「先客が落ちれば入れる」という
/// 意味になり、接続しっぱなしのまま黙って待つ消費者を作ってしまう。
/// 並列が欲しいなら、それはレーンを増やす話。
/// </remarks>
public sealed class RemoteConsumerConflictException : Exception
{
    public RemoteConsumerConflictException(string message)
        : base(message)
    {
    }
}
