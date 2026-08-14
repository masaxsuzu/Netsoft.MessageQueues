using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime;

/// <summary>
/// メッセージの発行の口。
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// メッセージをトピックへ発行する。返った時点で永続化は完了している。
    /// </summary>
    /// <returns>発行されたメッセージの識別子。</returns>
    /// <exception cref="InvalidOperationException">
    /// トピックに購読者が 1 つも居ない場合。誰にも配られないメッセージを受け付けても
    /// at-least-once の約束を果たす相手が居ないので、発行側の配線ミスとして弾く。
    /// </exception>
    Task<MessageId> PublishAsync(Topic topic, MessagePayload payload, CancellationToken cancellationToken);

    /// <summary>
    /// パーティションキー付きで発行する。同じキーのメッセージは、レーンを増やした購読でも
    /// 発行順に配送される。
    /// </summary>
    /// <exception cref="InvalidOperationException">トピックに購読者が 1 つも居ない場合。</exception>
    Task<MessageId> PublishAsync(
        Topic topic,
        MessagePayload payload,
        PartitionKey key,
        CancellationToken cancellationToken);
}
