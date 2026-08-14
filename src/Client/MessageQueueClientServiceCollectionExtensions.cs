using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Netsoft.MessageQueues.Runtime;

namespace Netsoft.MessageQueues.Client;

/// <summary>
/// 外から繋ぐ側の DI への登録の口。
/// </summary>
public static class MessageQueueClientServiceCollectionExtensions
{
    /// <summary>
    /// ブローカーへの発行の口と、登録済みの購読者を回す常駐を登録する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 購読者の登録は <see cref="MessageQueueServiceCollectionExtensions.AddMessageSubscriber{TSubscriber}"/>
    /// をそのまま使う ── <b>処理をどこで走らせるかだけが違い、書くものは同じ</b>。
    /// </para>
    /// <para>
    /// <see cref="MessageQueueServiceCollectionExtensions.AddMessageQueues"/> は呼ばない。
    /// あちらは配送エンジンと発行の口（プロセス内の実装）を組み立てるもので、
    /// 繋ぐだけの側には DB も配送ループも要らない。
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMessageQueueClient(
        this IServiceCollection services,
        Action<MessageQueueClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        MessageQueueClientOptions options = new();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<BrokerHttpClient>();

        // レーン数の検査と購読の重複の検査は、プロセス内と同じものを通す。
        services.TryAddSingleton(static provider =>
            new SubscriberRegistry(provider.GetServices<IMessageSubscriber>()));

        services.AddSingleton<IMessagePublisher, HttpMessagePublisher>();
        services.AddHostedService<RemoteSubscriberService>();

        return services;
    }
}
