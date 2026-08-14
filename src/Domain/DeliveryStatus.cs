namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// 配送の状態。購読ごとに 1 つ持つ。
/// </summary>
/// <remarks>
/// <para>
/// 状態は 2 つしか無い。<b>「配送中」を永続化しない</b>のが at-least-once の実装線で、
/// 購読者に渡している最中もディスクの上では <see cref="Pending"/> のまま残る。
/// プロセスがどこで死んでも、次の起動が見るのは Pending だけなので、
/// 起動時の復旧処理そのものが要らない ── InFlight を永続化する設計は、
/// それを Pending に戻す復旧と、復旧が二重に走らない仕組みを芋づるで要求する。
/// </para>
/// <para>
/// 代償は重複配送で、確認（<see cref="Delivered"/> への更新）の直前に死ぬと
/// 同じメッセージがもう一度届く。それは at-least-once の定義そのものなので、
/// 受ける側の契約として docs/operating.md に置いてある。
/// </para>
/// </remarks>
public enum DeliveryStatus
{
    /// <summary>未配送。まだ届いていないか、届いたが確認される前。</summary>
    Pending,

    /// <summary>配送済み。購読者が処理を終え、確認された。終端。</summary>
    Delivered,
}
