using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Runtime.Remote;

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
    /// <paramref name="configure"/> が効くのは、ホストが <see cref="MessageQueueOptions"/> を
    /// 自分で登録していないときだけ（登録は TryAdd で、先に置かれたものを上書きしない）。
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

        // options と registry は TryAdd。**ホストが先に登録していればそちらが勝つ。**
        // 設定ファイルから組み立てるホスト（Broker）は、値を DI の解決時まで読めないので
        // 工場として自分で登録する ── 起動時に読んで固めると、外から差し替える道が塞がる。
        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(static provider =>
            new SubscriberRegistry(provider.GetServices<IMessageSubscriber>()));
        services.AddSingleton<MessageQueueSignal>();
        services.AddSingleton<RemoteSubscriptionHub>();
        services.AddSingleton<IMessagePublisher, MessagePublisher>();
        services.AddSingleton<DeliveryEngine>();

        return services;
    }

    /// <summary>
    /// 配送エンジンを、ホストの寿命に合わせて回す常駐を登録する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 起動時にスキーマを用意し、停止時にはエンジンの完走を待つ
    /// （<see cref="DeliveryEngineHostedService"/>）。<b>これを呼べば
    /// <see cref="IMessageStore.InitializeAsync"/> も <see cref="DeliveryEngine.RunAsync"/> も
    /// 呼び出し側が触らなくてよい。</b>
    /// </para>
    /// <para>
    /// <see cref="AddMessageQueues"/> と分けてあるのは、<b>回さないホストが在るから</b>。
    /// 発行するだけのプロセスや、外から繋ぐだけの客は配送エンジンを持たない
    /// （配送するのは DB を持っている側 1 つだけ ── <c>docs/operating.md</c>）。
    /// 一緒にすると、繋ぐだけの側が黙って 2 本目の配送ループになる。
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMessageQueueEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<DeliveryEngineHostedService>();
        return services;
    }

    /// <summary>
    /// 購読者を 1 つ登録する。処理はこのプロセスの中で走る。
    /// </summary>
    public static IServiceCollection AddMessageSubscriber<TSubscriber>(this IServiceCollection services)
        where TSubscriber : class, IMessageSubscriber
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMessageSubscriber, TSubscriber>();
        return services;
    }

    /// <summary>
    /// プロセス外で処理される購読を 1 つ宣言する。
    /// </summary>
    /// <remarks>
    /// <b>宣言はここ（起動時）で、接続では増えない。</b>配送行は発行の瞬間に居た購読へ
    /// 向けて作られるので、繋いだ時点で購読が生まれる作りにすると、繋ぐまでに
    /// 発行されたぶんが誰にも配られない。宣言しておけば、消費者がまだ居なくても
    /// 未配送として積まれ、繋いだ時点で最初の 1 件から届く。
    /// </remarks>
    public static IServiceCollection AddRemoteSubscription(
        this IServiceCollection services,
        string topic,
        string name,
        int lanes = 1)
    {
        ArgumentNullException.ThrowIfNull(services);

        Topic parsedTopic = Topic.From(topic);
        SubscriptionName parsedName = SubscriptionName.From(name);

        // レーン数の検査は SubscriberRegistry が構築時に行う。ここで先回りすると
        // 上限を 2 か所に書くことになり、変えたときに片方だけ直る。
        services.AddSingleton<IMessageSubscriber>(provider => new RemoteSubscriber(
            parsedTopic,
            parsedName,
            lanes,
            provider.GetRequiredService<RemoteSubscriptionHub>()));

        return services;
    }
}
