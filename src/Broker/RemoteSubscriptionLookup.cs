using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Runtime;
using Netsoft.MessageQueues.Runtime.Remote;

namespace Netsoft.MessageQueues.Broker;

/// <summary>
/// 経路に現れた文字列を、宣言済みの遠隔購読とレーンへ解く。
/// </summary>
/// <remarks>
/// 口が 3 つ（配送・確認・失敗）とも同じ解き方を要るので括った。
/// 判断をエンドポイントへ写経すると、綴りの検査が 3 か所に散る
/// （<c>docs/conventions.md</c>「エンドポイントに置くのは…判断を書かない」）。
/// </remarks>
internal static class RemoteSubscriptionLookup
{
    /// <summary>解けなかった理由。解けたときは <see cref="IResult"/> が null になる。</summary>
    internal static bool TryResolve(
        SubscriberRegistry registry,
        string topic,
        string subscription,
        int lane,
        out Topic parsedTopic,
        out SubscriptionName parsedSubscription,
        out IResult? failure)
    {
        parsedSubscription = default;
        failure = null;

        if (!Topic.TryFrom(topic, out parsedTopic) ||
            !SubscriptionName.TryFrom(subscription, out parsedSubscription))
        {
            failure = TypedResults.BadRequest("トピック名または購読名が空です。");
            return false;
        }

        RemoteSubscriber? declared = registry.RemoteFor(parsedTopic, parsedSubscription);
        if (declared is null)
        {
            // 「まだ繋いでいないだけ」と区別できるように 404 にする。宣言されていない
            // 購読へ繋げてしまうと、綴り違いが「繋がったのに何も来ない」として現れる。
            failure = TypedResults.NotFound(
                $"購読 ({parsedTopic}, {parsedSubscription}) はプロセス外で処理される購読として宣言されていません。");
            return false;
        }

        if (lane < 0 || lane >= declared.Lanes)
        {
            failure = TypedResults.BadRequest(
                $"レーンは 0 以上 {declared.Lanes - 1} 以下です（この購読のレーン数は {declared.Lanes}）。");
            return false;
        }

        return true;
    }
}
