using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Infrastructure;

namespace Netsoft.MessageQueues.Runtime.Tests;

public sealed class MessageQueueServiceCollectionExtensionsTests : IDisposable
{
    private readonly TemporaryDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void 登録した部品一式が解決できる()
    {
        ServiceCollection services = new();
        services.AddSingleton<IMessageStore>(new SqliteMessageStore(_database.FilePath));
        services.AddSingleton<ILogger<DeliveryEngine>>(NullLogger<DeliveryEngine>.Instance);
        services.AddMessageQueues(options => options.RetryDelay = TimeSpan.FromMilliseconds(1));
        services.AddMessageSubscriber<OrdersSubscriber>();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IMessagePublisher>());
        Assert.NotNull(provider.GetRequiredService<DeliveryEngine>());
        Assert.Single(provider.GetRequiredService<SubscriberRegistry>().All);
        Assert.Equal(TimeSpan.FromMilliseconds(1), provider.GetRequiredService<MessageQueueOptions>().RetryDelay);
    }

    [Fact]
    public void 購読者を登録しなくても発行の口は解決できる()
    {
        ServiceCollection services = new();
        services.AddSingleton<IMessageStore>(new SqliteMessageStore(_database.FilePath));
        services.AddSingleton<ILogger<DeliveryEngine>>(NullLogger<DeliveryEngine>.Instance);
        services.AddMessageQueues();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IMessagePublisher>());
        Assert.Empty(provider.GetRequiredService<SubscriberRegistry>().All);
    }

    private sealed class OrdersSubscriber : IMessageSubscriber
    {
        public Topic Topic => Topic.From("orders");

        public SubscriptionName Name => SubscriptionName.From("billing");

        public Task HandleAsync(Message message, int attempt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
