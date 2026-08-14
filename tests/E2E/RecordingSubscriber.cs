using System.Threading.Channels;

using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Runtime;

namespace Netsoft.MessageQueues.E2E.Tests;

/// <summary>
/// 届いたメッセージを記録する購読者。最初の N 回をわざと失敗させられる。
/// </summary>
/// <remarks>
/// <b>プロセス内で使うものと 1 文字も違わない。</b>それがこの層で確かめたいことの
/// 半分で、利用者が書くものは処理がどこで走るかに依らない。
/// </remarks>
public sealed class RecordingSubscriber : IMessageSubscriber
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

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

    /// <summary>次に届いたメッセージを待つ。プロセスの起動を挟むので余裕を持たせてある。</summary>
    public async Task<(Message Message, int Attempt)> NextAsync()
    {
        using CancellationTokenSource timeout = new(Timeout);
        return await _received.Reader.ReadAsync(timeout.Token);
    }
}
