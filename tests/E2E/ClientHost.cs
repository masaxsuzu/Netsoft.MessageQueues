using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Netsoft.MessageQueues.Client;
using Netsoft.MessageQueues.Runtime;

namespace Netsoft.MessageQueues.E2E.Tests;

/// <summary>
/// ブローカーへ繋ぐ側のホスト。
/// </summary>
/// <remarks>
/// <b>利用者が書く配線と同じ形にしてある。</b>テストのためだけの近道
/// （購読者を直接呼ぶ、HTTP を手で叩く）を作ると、確かめているのが
/// 「利用者が書ける配線で動くか」でなくなる。
/// </remarks>
public sealed class ClientHost : IAsyncDisposable
{
    private readonly IHost _host;

    private ClientHost(IHost host)
    {
        _host = host;
        Publisher = host.Services.GetRequiredService<IMessagePublisher>();
    }

    /// <summary>発行の口。実装が HTTP になるだけで、型はプロセス内と同じ。</summary>
    public IMessagePublisher Publisher { get; }

    public static async Task<ClientHost> StartAsync(BrokerProcess broker, params IMessageSubscriber[] subscribers)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(subscribers);

        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);

        foreach (IMessageSubscriber subscriber in subscribers)
        {
            builder.Services.AddSingleton(subscriber);
        }

        builder.Services.AddLogging();
        builder.Services.AddMessageQueueClient(options =>
        {
            options.BaseAddress = broker.BaseAddress;
            options.ReconnectDelay = TimeSpan.FromMilliseconds(50);
        });

        IHost host = builder.Build();
        await host.StartAsync();
        return new ClientHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
