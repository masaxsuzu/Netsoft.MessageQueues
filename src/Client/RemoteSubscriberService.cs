using System.Net;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Netsoft.MessageQueues.Contracts;
using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Runtime;

namespace Netsoft.MessageQueues.Client;

/// <summary>
/// 登録された購読者を、ブローカーへ繋いで回す常駐。購読 × レーンごとに 1 本。
/// </summary>
/// <remarks>
/// <para>
/// <b>形はブローカー側の配送ループと同じ。</b>受け取る → 処理する → 確認する、の順で、
/// <b>確認は処理が正常に終わった後にしか送らない</b>。途中で落ちれば確認は届かず、
/// ブローカーが接続断として拾って再配送する ── at-least-once が
/// プロセスの境界を越えても同じ理由で成り立つ。
/// </para>
/// <para>
/// 繋ぎ直しを諦めない。ブローカーの再起動・一時的な失敗のどちらも、待って繋ぎ直せば
/// 続きから届く（未配送はブローカーの DB に残っている）。
/// </para>
/// </remarks>
public sealed class RemoteSubscriberService : BackgroundService
{
    private readonly BrokerHttpClient _broker;
    private readonly SubscriberRegistry _registry;
    private readonly MessageQueueClientOptions _options;
    private readonly ILogger<RemoteSubscriberService> _logger;

    public RemoteSubscriberService(
        BrokerHttpClient broker,
        SubscriberRegistry registry,
        MessageQueueClientOptions options,
        ILogger<RemoteSubscriberService> logger)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _broker = broker;
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_registry.All.SelectMany(subscriber =>
            Enumerable.Range(0, subscriber.Lanes)
                .Select(lane => RunLaneAsync(subscriber, lane, stoppingToken))));

    private async Task RunLaneAsync(IMessageSubscriber subscriber, int lane, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(subscriber, lane, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // 繋げない・切れたはどちらも待って繋ぎ直す。宣言の綴り違い（404）も
                // ここへ来るが、区別して諦めない ── ブローカーが正しい設定で
                // 立ち上がり直せば、そのまま繋がる。
                _logger.LogWarning(
                    exception,
                    "配送の受け取りが途切れました。繋ぎ直します。subscription={Subscription} lane={Lane}",
                    subscriber.Name,
                    lane);
            }

            await Task.Delay(_options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ConsumeAsync(IMessageSubscriber subscriber, int lane, CancellationToken cancellationToken)
    {
        string route = MessageQueueRoutes.DeliveryStream(subscriber.Topic.Value, subscriber.Name.Value, lane);

        using HttpResponseMessage response = await _broker.Http
            .GetAsync(route, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using DeliveryStream stream = await DeliveryStream.OpenAsync(response, cancellationToken)
            .ConfigureAwait(false);

        while (true)
        {
            DeliveryEvent? @event = await stream.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (@event is null)
            {
                return;
            }

            await ProcessAsync(subscriber, lane, @event, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessAsync(
        IMessageSubscriber subscriber,
        int lane,
        DeliveryEvent @event,
        CancellationToken cancellationToken)
    {
        Message message = new(
            MessageId.From(@event.MessageId),
            Topic.From(@event.Topic),
            MessagePayload.From(@event.Payload),
            @event.EnqueuedAt,
            @event.Key is null ? default : PartitionKey.From(@event.Key));

        try
        {
            await subscriber.HandleAsync(message, @event.Attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 停止。確認を送らないので、この 1 件は再配送される。
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "配送の処理に失敗しました。再配送を求めます。subscription={Subscription} message={MessageId} attempt={Attempt}",
                subscriber.Name,
                message.Id,
                @event.Attempt);

            await SettleAsync(
                MessageQueueRoutes.Nack(
                    subscriber.Topic.Value, subscriber.Name.Value, lane, message.Id.Value, exception.Message),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await SettleAsync(
            MessageQueueRoutes.Ack(subscriber.Topic.Value, subscriber.Name.Value, lane, message.Id.Value),
            cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// 送れなかったときに投げ直さない。**確認が届かなかったメッセージは再配送される**ので、
    /// ここで例外にすると、既に処理し終えたものを理由に接続を落とすことになる。
    /// 記録だけ残して次の 1 件へ進む。
    /// </remarks>
    private async Task SettleAsync(string route, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _broker.Http
            .PostAsync(route, content: null, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            _logger.LogWarning("確認が受け付けられませんでした（{StatusCode}）。route={Route}", response.StatusCode, route);
        }
    }
}
