using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime;

/// <summary>
/// プロセス内の購読者の一覧。起動時に確定し、以後変わらない。
/// </summary>
/// <remarks>
/// <para>
/// 実行中の購読の追加・削除は持たない。配送行は発行の瞬間に居た購読へ向けて作られる
/// （<see cref="IMessageStore.AppendAsync"/>）ので、動的に増減させると「発行時には居たが
/// もう居ない購読」の未配送行が永遠に残り、「発行時には居なかった購読」は過去のメッセージを
/// 受け取れない ── どちらの意味論も選ばずに済むのは、一覧が静的な間だけ。
/// </para>
/// <para>
/// 重複（同じトピックに同じ購読名）は構築時に落とす。配送行の主キーが
/// (MessageId, Subscription) なので、重複したまま走らせると 2 つ目の購読者が
/// 1 つ目の確認済みの行を見て、どちらか片方にしか配られない。
/// </para>
/// </remarks>
public sealed class SubscriberRegistry
{
    private readonly IReadOnlyList<IMessageSubscriber> _subscribers;

    /// <exception cref="ArgumentException">
    /// トピックか購読名が空の購読者、または同じ (トピック, 購読名) の購読者が複数居る場合。
    /// </exception>
    public SubscriberRegistry(IEnumerable<IMessageSubscriber> subscribers)
    {
        ArgumentNullException.ThrowIfNull(subscribers);

        List<IMessageSubscriber> all = [.. subscribers];
        HashSet<(string Topic, string Name)> seen = [];

        foreach (IMessageSubscriber subscriber in all)
        {
            if (subscriber.Topic.IsEmpty || subscriber.Name.IsEmpty)
            {
                throw new ArgumentException(
                    $"購読者 {subscriber.GetType().Name} のトピックまたは購読名が空です。",
                    nameof(subscribers));
            }

            if (!seen.Add((subscriber.Topic.Value, subscriber.Name.Value)))
            {
                throw new ArgumentException(
                    $"購読 ({subscriber.Topic}, {subscriber.Name}) が重複しています。",
                    nameof(subscribers));
            }
        }

        _subscribers = all;
    }

    /// <summary>登録されている購読者すべて。</summary>
    public IReadOnlyList<IMessageSubscriber> All => _subscribers;

    /// <summary>指定されたトピックを購読している購読者。</summary>
    public IReadOnlyList<IMessageSubscriber> For(Topic topic) =>
        [.. _subscribers.Where(s => s.Topic == topic)];
}
