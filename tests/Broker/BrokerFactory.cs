using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Netsoft.MessageQueues.Broker.Tests;

/// <summary>
/// テスト 1 件ごとのブローカー。使い捨ての DB と、宣言済みの遠隔購読を持つ。
/// </summary>
/// <remarks>
/// 設定は環境変数ではなく <c>ConfigureAppConfiguration</c> のメモリ源から入れる。
/// 環境変数はプロセス全域に効くので、xUnit がクラスをまたいで並行に走らせると
/// 別のテストの DB を掴む。
/// </remarks>
public sealed class BrokerFactory : WebApplicationFactory<Program>
{
    private readonly string _directory;
    private readonly IReadOnlyList<(string Topic, string Name, int Lanes)> _subscriptions;

    public BrokerFactory(params (string Topic, string Name, int Lanes)[] subscriptions)
    {
        // 並行するテストと衝突しないよう、インスタンスごとに分ける（docs/build.md）。
        _directory = Path.Combine(Path.GetTempPath(), "netsoft-messagequeues-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "messages.db");
        _subscriptions = subscriptions;
    }

    /// <summary>この ブローカーが使う DB ファイルのパス。</summary>
    public string DatabasePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Dictionary<string, string?> settings = new()
        {
            ["MessageQueue:DatabasePath"] = DatabasePath,

            // 再配送の待ちを短くする。既定の 5 秒はテストの時間予算に対して長すぎる。
            ["MessageQueue:RetryDelaySeconds"] = "0.05",
        };

        for (int i = 0; i < _subscriptions.Count; i++)
        {
            (string topic, string name, int lanes) = _subscriptions[i];
            settings[$"MessageQueue:Subscriptions:{i}:Topic"] = topic;
            settings[$"MessageQueue:Subscriptions:{i}:Name"] = name;
            settings[$"MessageQueue:Subscriptions:{i}:Lanes"] = lanes.ToString();
        }

        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // プールを閉じてから消す。閉じるのは自分の DB のプールだけ
        // （理由は docs/build.md「テストの後始末」）。
        using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
        SqliteConnection.ClearPool(connection);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末に失敗してもテストの結果を変えたくない。
        }
    }
}
