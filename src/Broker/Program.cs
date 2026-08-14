using Microsoft.Extensions.Options;

using Netsoft.MessageQueues.Broker;
using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Infrastructure;
using Netsoft.MessageQueues.Runtime;
using Netsoft.MessageQueues.Runtime.Remote;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BrokerOptions>(builder.Configuration.GetSection(BrokerOptions.SectionName));

// **設定は解決の時点で読む。ここ（Build の前）で読んで固めない。**
// 最上位ステートメントの構成は WebApplicationFactory の差し替えより先に走るので、
// 起動時に読んだ値はテストから変えられない ── DB の場所も購読の宣言も差し替えられず、
// 口の検証に本物のポートと本物のファイルが要ることになる。
builder.Services.AddSingleton<IMessageStore>(provider =>
    new SqliteMessageStore(Options(provider).DatabasePath));

builder.Services.AddSingleton(provider => new MessageQueueOptions
{
    RetryDelay = TimeSpan.FromSeconds(Options(provider).RetryDelaySeconds),
});

// プロセス外で処理される購読は、この一覧が確定した時点で決まり、接続では増えない。
builder.Services.AddSingleton(provider => new SubscriberRegistry([
    .. provider.GetServices<IMessageSubscriber>(),
    .. Options(provider).Subscriptions.Select(subscription => new RemoteSubscriber(
        Topic.From(subscription.Topic),
        SubscriptionName.From(subscription.Name),
        subscription.Lanes,
        provider.GetRequiredService<RemoteSubscriptionHub>())),
]));

// 上の 3 つを先に置いてあるので、AddMessageQueues の TryAdd はこれらを上書きしない。
builder.Services.AddMessageQueues();

// 錠を先に登録する。掴むのは StartingAsync なので順序に依らず一番外側で効くが、
// 「まず錠、それから配送」という読み順をコードにも残しておく。
builder.Services.AddHostedService<BrokerLockService>();
builder.Services.AddHostedService<DeliveryEngineHostedService>();

WebApplication app = builder.Build();
app.MapMessageQueue();
app.Run();

static BrokerOptions Options(IServiceProvider provider) =>
    provider.GetRequiredService<IOptions<BrokerOptions>>().Value;

/// <summary>
/// テストから <c>WebApplicationFactory</c> で起こすための入口。
/// </summary>
/// <remarks>
/// 最上位ステートメントが生成する Program は internal なので、明示して公開する。
/// これが無いと、口の検証に本物のポートを開けて待つことになる。
/// </remarks>
public partial class Program;
