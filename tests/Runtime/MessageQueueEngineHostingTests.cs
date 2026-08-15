using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Infrastructure;

namespace Netsoft.MessageQueues.Runtime.Tests;

public sealed class MessageQueueEngineHostingTests : IDisposable
{
    private static readonly Topic Orders = Topic.From("orders");

    private readonly TemporaryDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ホストに載せるだけで発行から配送まで動く()
    {
        RecordingSubscriber billing = new("orders", "billing");
        using IHost host = BuildHost(billing);

        await host.StartAsync();

        // InitializeAsync も RunAsync も呼んでいない。どちらも常駐がやっている。
        MessageId id = await host.Services.GetRequiredService<IMessagePublisher>()
            .PublishAsync(Orders, MessagePayload.From("""{"orderId":42}"""), CancellationToken.None);

        Assert.Equal(id, (await billing.NextAsync()).Message.Id);

        await host.StopAsync();
    }

    [Fact]
    public async Task 停止は2度呼ばれても壊れない()
    {
        using IHost host = BuildHost(new RecordingSubscriber("orders", "billing"));

        await host.StartAsync();
        await host.StopAsync();

        // 破棄と停止の順序はホストによって違う（WebApplicationFactory は破棄してから止める）。
        // 2 度目が例外にならないことを、この層で固定しておく。
        await host.StopAsync();
    }

    [Fact]
    public async Task 起動していないまま停止しても壊れない()
    {
        using IHost host = BuildHost(new RecordingSubscriber("orders", "billing"));

        await host.StopAsync();
    }

    private IHost BuildHost(IMessageSubscriber subscriber)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);

        builder.Services.AddLogging();
        builder.Services.AddSingleton<IMessageStore>(new SqliteMessageStore(_database.FilePath));
        builder.Services.AddSingleton(subscriber);

        // 再配送の待ちを短くする。既定の 5 秒はテストの時間予算に対して長すぎる。
        builder.Services.AddMessageQueues(options => options.RetryDelay = TimeSpan.FromMilliseconds(25));
        builder.Services.AddMessageQueueEngine();

        return builder.Build();
    }
}
