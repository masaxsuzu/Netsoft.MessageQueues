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
/// 配送はレーン（<see cref="Lanes"/>）の中で直列。既定はレーン 1 本で、購読全体が発行順。
/// レーンを増やすと購読の中が並列になるが、同じパーティションキーは同じレーンへ落ちるので
/// 「同じキーの中は発行順」は保たれる。<b>同じメッセージも常に同じレーンへ落ちる</b>ため、
/// レーンを増やしても同じメッセージが並行に処理されることは無い（同一ホスト前提）──
/// 冪等性が相手にするのは逐次的な重複だけでよい。
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

    /// <summary>この購読を何本のレーンで並列に処理するか。既定 1（購読全体が発行順）。</summary>
    /// <remarks>
    /// 上限は <see cref="SubscriberRegistry.MaxLanes"/>（構築時に検査される）。
    /// レーン数を変えると既存メッセージのレーン割り当ても変わる（割る数が変わる）が、
    /// どのレーンも Pending を拾い直すので取り残しは出ない ── 変わるのは順序の並びだけで、
    /// それは「キーをまたぐ順序は約束しない」の範囲に収まる。
    /// </remarks>
    int Lanes => 1;

    /// <summary>
    /// メッセージを 1 件処理する。正常に返すと配送が確認され、例外を投げると再配送される。
    /// </summary>
    /// <param name="message">配送されたメッセージ。</param>
    /// <param name="attempt">何度目の試行か（1 始まり）。</param>
    /// <param name="cancellationToken">エンジンの停止。中断しても再配送されるので握りつぶさないこと。</param>
    Task HandleAsync(Message message, int attempt, CancellationToken cancellationToken);
}
