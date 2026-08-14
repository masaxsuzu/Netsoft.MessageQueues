namespace Netsoft.MessageQueues.Broker;

/// <summary>
/// ブローカーの設定。<c>appsettings.json</c> の <c>MessageQueue</c> 節から読む。
/// </summary>
public sealed class BrokerOptions
{
    /// <summary>設定の節の名前。</summary>
    public const string SectionName = "MessageQueue";

    /// <summary>SQLite の DB ファイルの場所。</summary>
    public string DatabasePath { get; set; } = "messages.db";

    /// <summary>配送に失敗してから再配送するまでの待ち（秒）。</summary>
    public double RetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// プロセス外で処理される購読の宣言。
    /// </summary>
    /// <remarks>
    /// <b>ここに書いたものが購読のすべてで、接続では増えない。</b>配送行は発行の
    /// 瞬間に居た購読へ向けて作られるので、繋いだ時点で購読が生まれる作りにすると、
    /// 繋ぐまでに発行されたぶんが誰にも配られない（<c>docs/operating.md</c>）。
    /// 宣言しておけば、消費者がまだ居なくても未配送として積まれる。
    /// </remarks>
    public IList<RemoteSubscriptionOptions> Subscriptions { get; set; } = [];
}

/// <summary>プロセス外で処理される購読 1 つぶんの宣言。</summary>
public sealed class RemoteSubscriptionOptions
{
    /// <summary>購読するトピック。</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>購読の名前。同じトピックの中で一意であること。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>並列に処理するレーンの数。既定 1。</summary>
    public int Lanes { get; set; } = 1;
}
