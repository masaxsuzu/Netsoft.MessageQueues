using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime;

/// <summary>
/// メッセージの購読者。利用者が書く側で、この基盤にとっての唯一の拡張点。
/// </summary>
/// <remarks>
/// <para>
/// <b>at-least-once の受け側の契約</b>（docs/operating.md）:
/// <see cref="HandleAsync"/> が正常に返って初めて配送が確認される。例外を投げれば
/// 同じメッセージが再配送され、確認の直前にプロセスが落ちても再配送される。
/// つまり<b>同じメッセージが 2 度届くのは異常ではない</b>。処理は冪等に書くこと。
/// <see cref="HandleAsync"/> の <c>attempt</c> は何度目の試行かを伝えるが、重複の検出には使えない
/// （1 度目の配送が「処理は済んだが確認の前に落ちた」でも、次は 2 度目として届く）。
/// </para>
/// <para>
/// 同じ購読への配送は直列（順に 1 件ずつ）。前のメッセージの処理が終わるまで
/// 次は届かない。並列にしたい場合は購読を分ける。
/// </para>
/// </remarks>
public interface IMessageSubscriber
{
    /// <summary>購読するトピック。</summary>
    Topic Topic { get; }

    /// <summary>購読の名前。同じトピックの中で一意であること。</summary>
    /// <remarks>
    /// この名前が配送の宛先としてディスクに残る。名前を変えると別の購読になり、
    /// 旧名義の未配送分は誰にも配られなくなる（<see cref="SubscriptionName"/> の注記）。
    /// </remarks>
    SubscriptionName Name { get; }

    /// <summary>
    /// メッセージを 1 件処理する。正常に返すと配送が確認され、例外を投げると再配送される。
    /// </summary>
    /// <param name="message">配送されたメッセージ。</param>
    /// <param name="attempt">何度目の試行か（1 始まり）。</param>
    /// <param name="cancellationToken">エンジンの停止。中断しても再配送されるので握りつぶさないこと。</param>
    Task HandleAsync(Message message, int attempt, CancellationToken cancellationToken);
}
