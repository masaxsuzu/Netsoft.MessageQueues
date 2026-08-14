using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime;

/// <summary>
/// <see cref="IMessagePublisher"/> の実装。永続化してから合図を鳴らす。
/// </summary>
/// <remarks>
/// 順序が肝で、<b>合図は永続化の後</b>。先に鳴らすと、起きたエンジンが
/// まだ書かれていないメッセージを探して空振りし、合図は消費済みなので
/// そのメッセージは次の発行まで配られない。逆順なら、最悪でも
/// 「書いたが合図の前に落ちた」で、次の起動時にエンジンが未配送を拾う。
/// </remarks>
public sealed class MessagePublisher : IMessagePublisher
{
    private readonly IMessageStore _store;
    private readonly SubscriberRegistry _registry;
    private readonly MessageQueueSignal _signal;
    private readonly TimeProvider _timeProvider;

    public MessagePublisher(
        IMessageStore store,
        SubscriberRegistry registry,
        MessageQueueSignal signal,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _registry = registry;
        _signal = signal;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<MessageId> PublishAsync(
        Topic topic,
        MessagePayload payload,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IMessageSubscriber> subscribers = _registry.For(topic);
        if (subscribers.Count == 0)
        {
            throw new InvalidOperationException(
                $"トピック {topic} に購読者が居ません。発行しても誰にも配られないため、受け付けません。");
        }

        // 採番は GUID。発行の口が複数スレッドから同時に呼ばれても衝突しない、が要件のすべてで、
        // 順序は Messages テーブルの Seq が持つ（SqliteMessageStore の注記）ので ID に順序は要らない。
        Message message = new(
            MessageId.From(Guid.NewGuid().ToString("N")),
            topic,
            payload,
            _timeProvider.GetUtcNow());

        await _store.AppendAsync(
            message,
            [.. subscribers.Select(s => s.Name)],
            cancellationToken).ConfigureAwait(false);

        foreach (IMessageSubscriber subscriber in subscribers)
        {
            _signal.Set(topic, subscriber.Name);
        }

        return message.Id;
    }
}
