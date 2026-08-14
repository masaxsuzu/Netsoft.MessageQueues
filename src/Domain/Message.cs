namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// 発行されたメッセージ。トピックとペイロードと発行時刻を持つ、不変の値。
/// </summary>
/// <remarks>
/// 状態を持たない。「どこまで配れたか」はメッセージではなく配送
/// （<see cref="Delivery"/>。購読ごとに 1 行）の持ち物で、
/// ここに混ぜると複数購読で同じメッセージの状態が購読の数だけ要ることになる。
/// </remarks>
public sealed record Message
{
    public Message(MessageId id, Topic topic, MessagePayload payload, DateTimeOffset enqueuedAt)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Message には識別子が必要です。", nameof(id));
        }

        if (topic.IsEmpty)
        {
            throw new ArgumentException("Message にはトピックが必要です。", nameof(topic));
        }

        if (payload.IsEmpty)
        {
            throw new ArgumentException("Message にはペイロードが必要です。", nameof(payload));
        }

        Id = id;
        Topic = topic;
        Payload = payload;
        EnqueuedAt = enqueuedAt;
    }

    /// <summary>メッセージの識別子。</summary>
    public MessageId Id { get; }

    /// <summary>発行先のトピック。</summary>
    public Topic Topic { get; }

    /// <summary>検証済みのペイロード（JSON・64KB 以下）。</summary>
    public MessagePayload Payload { get; }

    /// <summary>発行された時刻。</summary>
    public DateTimeOffset EnqueuedAt { get; }
}
