using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime;

/// <summary>
/// メッセージキュー基盤の DI への登録の口。
/// </summary>
public static class MessageQueueServiceCollectionExtensions
{
    /// <summary>
    /// 発行と配送の部品一式を登録する。
    /// </summary>
    /// <remarks>
    /// <see cref="IMessageStore"/> は登録しない ── 実装（SQLite）は Infrastructure に居て、
    /// Runtime はそれを参照しない。呼び出し側（ホスト）が
    /// <c>services.AddSingleton&lt;IMessageStore&gt;(new SqliteMessageStore(path))</c> のように
    /// 自分で選んで登録する。層の向きを DI の都合で逆流させないための線
    /// （Netsoft.Jobs が配線を Web に置いているのと同じ判断）。
    /// </remarks>
    public static IServiceCollection AddMessageQueues(
        this IServiceCollection services,
        Action<MessageQueueOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        MessageQueueOptions options = new();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(static provider =>
            new SubscriberRegistry(provider.GetServices<IMessageSubscriber>()));
        services.AddSingleton<MessageQueueSignal>();
        services.AddSingleton<IMessagePublisher, MessagePublisher>();
        services.AddSingleton<DeliveryEngine>();

        return services;
    }

    /// <summary>
    /// 購読者を 1 つ登録する。
    /// </summary>
    public static IServiceCollection AddMessageSubscriber<TSubscriber>(this IServiceCollection services)
        where TSubscriber : class, IMessageSubscriber
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMessageSubscriber, TSubscriber>();
        return services;
    }
}
