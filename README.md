# Netsoft.MessageQueues

単一コンピュータ上で動作するメッセージキュー基盤。永続化されたメッセージを
at-least-once で配送する。購読者は**同じプロセスの中でも、別のプロセスでもよい**。

- メッセージは SQLite に永続化され、プロセスが落ちても未配送分は失われない
- 1 つのメッセージを単一または複数の購読者へ配送できる（購読ごとに独立に進む）
- **同じメッセージが 2 度届くことがある**（at-least-once）。購読者は冪等に書く
- ペイロードは JSON で、UTF-8 で 64KB まで
- 順序は**パーティションキーごとに発行順**。既定（レーン 1 本）では購読全体が発行順で、
  購読者が `Lanes` を増やすとキーを保ったまま並列になる（[docs/operating.md](./docs/operating.md)）

## 使う

購読者を書く。`IMessageSubscriber` がこの基盤にとっての唯一の拡張点。

```csharp
public sealed class BillingSubscriber : IMessageSubscriber
{
    public Topic Topic => Topic.From("orders");
    public SubscriptionName Name => SubscriptionName.From("billing");

    public Task HandleAsync(Message message, int attempt, CancellationToken cancellationToken)
    {
        // 正常に返せば配送済みになる。例外を投げれば再配送される。
        // 同じメッセージが 2 度届いても結果が変わらないように書くこと（docs/operating.md）。
        return Task.CompletedTask;
    }
}
```

配線して動かす。store（SQLite）の登録だけはホストの仕事。

```csharp
ServiceCollection services = new();
services.AddSingleton<IMessageStore>(new SqliteMessageStore("messages.db"));
services.AddMessageQueues();
services.AddMessageSubscriber<BillingSubscriber>();
// ILogger<DeliveryEngine> はホストのログ基盤から（無ければ NullLogger でよい）

await using ServiceProvider provider = services.BuildServiceProvider();
await provider.GetRequiredService<IMessageStore>().InitializeAsync(CancellationToken.None);

using CancellationTokenSource stop = new();
Task run = provider.GetRequiredService<DeliveryEngine>().RunAsync(stop.Token);

IMessagePublisher publisher = provider.GetRequiredService<IMessagePublisher>();
await publisher.PublishAsync(
    Topic.From("orders"),
    MessagePayload.From("""{"orderId":42}"""),
    CancellationToken.None);

// 順序を守りたい単位があるならパーティションキーを付ける。購読者が Lanes を
// 増やして並列にしても、同じキーの中は発行順のまま（docs/operating.md）。
await publisher.PublishAsync(
    Topic.From("orders"),
    MessagePayload.From("""{"orderId":42,"step":"paid"}"""),
    PartitionKey.From("order-42"),
    CancellationToken.None);

// 終了時は取り消して待つ。確認前の配送は次の起動で再配送される。
stop.Cancel();
await run;
```

## 別のプロセスから使う

ブローカーを 1 つ立てる。処理をどこで走らせるかだけが違い、約束（at-least-once・順序・
64KB）は同じ。プロセス外で処理する購読は**起動時に宣言する**（接続では増えない）。

```jsonc
// src/Broker/appsettings.json
{
  "MessageQueue": {
    "DatabasePath": "messages.db",
    "Subscriptions": [ { "Topic": "orders", "Name": "billing", "Lanes": 1 } ]
  }
}
```

```bash
cd src/Broker && dotnet run    # 既定 :5000
```

発行は本体をペイロードそのものにして POST する。

```bash
curl -X POST 'http://localhost:5000/topics/orders/messages?key=order-42' \
     -H 'Content-Type: application/json' -d '{"orderId":42}'
# => {"messageId":"..."}
```

受け取りは SSE で、処理が終わったら ack を返す。**ack するまで次は流れない。**

```bash
curl -N http://localhost:5000/subscriptions/orders/billing/lanes/0/deliveries
# event: delivery
# data: {"messageId":"...","topic":"orders","key":"order-42","attempt":1,"payload":"{\"orderId\":42}"}

curl -X POST http://localhost:5000/subscriptions/orders/billing/lanes/0/deliveries/<messageId>/ack
```

- **1 接続 = 1 レーン。** 並列に処理したい客はレーンの数だけ繋ぐ（同じレーンへ 2 本目は 409）
- **ack を返す前に接続が切れたら再配送される。** 客のプロセスが落ちても失われない
- 失敗を伝えるなら `/nack`（`?reason=` を付けられる）。待ちを挟んで同じメッセージが再び届く
- 詳細は [docs/operating.md](./docs/operating.md)

### C# から繋ぐ

`src/Client` を使うと、SSE も ack も自分で書かずに済む。**購読者は上とまったく同じ
`IMessageSubscriber`** で、処理がどのプロセスで走るかだけが違う。

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessageSubscriber<BillingSubscriber>();   // 上と同じ登録の口
builder.Services.AddMessageQueueClient(options =>
    options.BaseAddress = new Uri("http://localhost:5000"));

using IHost host = builder.Build();

// 発行も同じ口（IMessagePublisher）。実装が HTTP になるだけ。
IMessagePublisher publisher = host.Services.GetRequiredService<IMessagePublisher>();
await publisher.PublishAsync(
    Topic.From("orders"), MessagePayload.From("""{"orderId":42}"""), CancellationToken.None);

await host.RunAsync();   // 購読は常駐が回す。接続が切れても繋ぎ直す
```

- ブローカー側に**同じ購読が宣言されていること**が前提（宣言は起動時。接続では増えない）
- 購読者が例外を投げれば `nack` が飛び、待ちを挟んで再配送される
- ブローカーを再起動しても客は繋ぎ直し、未配送のぶんから続く

## 開発

```bash
dotnet build
dotnet test
```

**.NET 10 SDK が必要**（`global.json` で固定）。
開発サイクル・規約は [docs/](./docs/) にある。入口は [CLAUDE.md](./CLAUDE.md)。

**スキーマに互換コードは無い。** 更新をまたいでテーブルの形が変わったら、
古い DB ファイルを消してから起動する（Netsoft.Jobs と同じ判断）。
