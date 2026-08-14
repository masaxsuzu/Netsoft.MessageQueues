using Microsoft.Extensions.Logging;

using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime;

/// <summary>
/// 配送エンジン。購読ごとに 1 本のループを立て、未配送のメッセージを順に購読者へ渡す。
/// </summary>
/// <remarks>
/// <para>
/// <b>ループはレーンごとに 1 本、レーンの中では増やさない。</b>レーンの中を並列にすると
/// 順序の約束の単位が消え、取得の競り合いも生まれる。並列はレーンの数
/// （<see cref="IMessageSubscriber.Lanes"/>、既定 1）で決まり、メッセージは
/// パーティションのハッシュでレーンへ振り分けられる ── 同じキーは同じレーンなので、
/// レーンの中の直列がそのまま「同じキーの中は発行順」になる。
/// </para>
/// <para>
/// at-least-once はループの形そのもの。取得 → 処理 → 確認、の順で、
/// <b>確認は処理が正常に終わった後にしか書かれない</b>。どの隙間でプロセスが死んでも、
/// ディスクに残るのは Pending の行だけなので、次の起動でこのループが拾い直す。
/// 起動時の復旧処理は存在しない ── 復旧すべき中間状態を永続化していないから
/// （<see cref="DeliveryStatus"/> の注記）。
/// </para>
/// </remarks>
public sealed class DeliveryEngine
{
    private readonly IMessageStore _store;
    private readonly SubscriberRegistry _registry;
    private readonly MessageQueueSignal _signal;
    private readonly MessageQueueOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeliveryEngine> _logger;

    public DeliveryEngine(
        IMessageStore store,
        SubscriberRegistry registry,
        MessageQueueSignal signal,
        MessageQueueOptions options,
        TimeProvider timeProvider,
        ILogger<DeliveryEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _registry = registry;
        _signal = signal;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// 全購読・全レーンの配送ループを走らせる。<paramref name="cancellationToken"/> が
    /// 取り消されるまで返らない。取り消しで正常に完了する（例外にしない）。
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(_registry.All.SelectMany(
            s => Enumerable.Range(0, s.Lanes).Select(
                index => RunLaneAsync(s, new Lane(index, s.Lanes), cancellationToken))));

    private async Task RunLaneAsync(
        IMessageSubscriber subscriber,
        Lane lane,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                ClaimedDelivery? claimed = await _store
                    .TryClaimNextAsync(subscriber.Topic, subscriber.Name, lane, cancellationToken)
                    .ConfigureAwait(false);

                if (claimed is null)
                {
                    await _signal.WaitAsync(subscriber.Topic, subscriber.Name, lane.Index, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await subscriber.HandleAsync(claimed.Message, claimed.Attempt, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 停止による中断。確認は書かれていないので、この 1 件は次の起動で再配送される。
                    return;
                }
                catch (Exception exception)
                {
                    // 購読者の失敗はエンジンを殺さない。行は Pending のまま残っているので、
                    // 待ってから同じループが拾い直す（順序を保つため、次のメッセージへ進まない）。
                    _logger.LogWarning(
                        exception,
                        "配送に失敗しました。再配送します。topic={Topic} subscription={Subscription} message={MessageId} attempt={Attempt}",
                        subscriber.Topic,
                        subscriber.Name,
                        claimed.Message.Id,
                        claimed.Attempt);

                    await Task.Delay(_options.RetryDelay, _timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                await _store.MarkDeliveredAsync(claimed.Message.Id, subscriber.Name, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 停止は正常系。呼び出し側の Task.WhenAll を例外で汚さない。
        }
    }
}
