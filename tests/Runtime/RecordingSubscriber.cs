using System.Threading.Channels;

using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime.Tests;

/// <summary>
/// 届いたメッセージを記録する購読者。最初の N 回をわざと失敗させられる。
/// </summary>
public sealed class RecordingSubscriber : IMessageSubscriber
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Channel<(Message Message, int Attempt)> _received =
        Channel.CreateUnbounded<(Message, int)>();

    private int _failuresRemaining;

    public RecordingSubscriber(string topic, string name, int failFirstAttempts = 0, int lanes = 1)
    {
        Topic = Topic.From(topic);
        Name = SubscriptionName.From(name);
        _failuresRemaining = failFirstAttempts;
        Lanes = lanes;
    }

    public Topic Topic { get; }

    public SubscriptionName Name { get; }

    public int Lanes { get; }

    public Task HandleAsync(Message message, int attempt, CancellationToken cancellationToken)
    {
        if (_failuresRemaining > 0)
        {
            _failuresRemaining--;
            throw new InvalidOperationException("テストのための故意の失敗。");
        }

        _received.Writer.TryWrite((message, attempt));
        return Task.CompletedTask;
    }

    /// <summary>次に届いたメッセージを待つ。10 秒で諦める（フックしたテストを永遠に待たせない）。</summary>
    public async Task<(Message Message, int Attempt)> NextAsync()
    {
        using CancellationTokenSource timeout = new(Timeout);
        return await _received.Reader.ReadAsync(timeout.Token);
    }

    /// <summary>まだ何も届いていないか。「届かないこと」の検証は届いた側の検証の後に行うこと。</summary>
    public bool HasReceivedNothing => !_received.Reader.TryPeek(out _);
}
