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
    public Message(
        MessageId id,
        Topic topic,
        MessagePayload payload,
        DateTimeOffset enqueuedAt,
        PartitionKey key = default)
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
        Key = key;
    }

    /// <summary>メッセージの識別子。</summary>
    public MessageId Id { get; }

    /// <summary>発行先のトピック。</summary>
    public Topic Topic { get; }

    /// <summary>検証済みのペイロード（JSON・64KB 以下）。</summary>
    public MessagePayload Payload { get; }

    /// <summary>発行された時刻。</summary>
    public DateTimeOffset EnqueuedAt { get; }

    /// <summary>パーティションキー。無くてもよい（<see cref="PartitionKey.IsEmpty"/>）。</summary>
    public PartitionKey Key { get; }

    /// <summary>
    /// レーンの割り当てに使うハッシュ。キーがあればキーから、無ければ識別子から決まる。
    /// </summary>
    /// <remarks>
    /// キーの無いメッセージを 1 つの固定レーンへ寄せない。寄せると、キーを使う購読で
    /// 「キー無しのメッセージだけが 1 レーンに詰まる」偏りになる。識別子で散らせば
    /// 順序を約束しないもの同士が全レーンへ均される。
    /// </remarks>
    public long PartitionHash => Key.IsEmpty ? StableHash.OfUtf8(Id.Value) : Key.Hash;
}
