using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime.Tests;

public sealed class MessageQueueSignalTests
{
    private static readonly Topic Orders = Topic.From("orders");
    private static readonly SubscriptionName Billing = SubscriptionName.From("billing");
    private static readonly SubscriptionName Audit = SubscriptionName.From("audit");

    [Fact]
    public async Task 待ちより先に鳴った合図は取りこぼさない()
    {
        MessageQueueSignal signal = new();

        // 発行（Set）が先、エンジンの待ちが後。この順でも即座に返ることが、
        // エンジンが安全網のポーリング無しで合図だけに頼れる根拠（型の注記）。
        signal.Set(Orders, Billing);

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await signal.WaitAsync(Orders, Billing, timeout.Token);
    }

    [Fact]
    public async Task 合図は名指しした購読にしか届かない()
    {
        MessageQueueSignal signal = new();

        signal.Set(Orders, Billing);

        using CancellationTokenSource cancelled = new(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            signal.WaitAsync(Orders, Audit, cancelled.Token));
    }

    [Fact]
    public void 満杯の箱への合図は何もしないで返る()
    {
        MessageQueueSignal signal = new();

        // 容量 1 なので 2 つ目からはあふれるが、待たされも例外にもならないこと。
        signal.Set(Orders, Billing);
        signal.Set(Orders, Billing);
        signal.Set(Orders, Billing);
    }
}
