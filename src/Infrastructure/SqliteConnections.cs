using Microsoft.Data.Sqlite;

namespace Netsoft.MessageQueues.Infrastructure;

/// <summary>
/// SQLite 接続のイディオムの共有。接続文字列の組み立てと、開く処理の後始末。
/// </summary>
/// <remarks>
/// 接続の作法（呼び出しごとに開く・プールに任せる）はストアごとに変えるものではなく、
/// 直すときは一度に直したい。Netsoft.Jobs の同名クラスと同じ判断。
/// </remarks>
internal static class SqliteConnections
{
    /// <summary>ロックが空くのを待つ上限。</summary>
    /// <remarks>
    /// <para>
    /// <b>Microsoft.Data.Sqlite の既定（30 秒）に任せない。</b>あれは接続文字列を書かなければ
    /// 効く値で、コードのどこにも「どれだけ待つつもりか」が残らない。ここに置くのは
    /// 待ちの長さを決めているのが誰かを 1 か所にするため。
    /// </para>
    /// <para>
    /// 5 秒にしてあるのは、WAL の下で待たされる相手が<b>単一の書き手のロック</b>だけだから。
    /// このアセンブリの書き込みは数行の INSERT / UPDATE で、保持は 1 ミリ秒に満たない。
    /// それを 5 秒待って空かないなら混雑ではなく詰まりなので、例外にして
    /// 呼び出し側の再試行とログに載せたほうが早く分かる。
    /// </para>
    /// </remarks>
    private static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(5);

    /// <summary>DB ファイルのパスから接続文字列を組み立てる。</summary>
    /// <remarks>
    /// <b>待ちの設定をここへ書かない。</b>接続文字列はプールの鍵そのもので、1 文字でも
    /// 違えば別のプールに当たる。テストの後始末は <c>DataSource</c> だけを組み立てた文字列で
    /// <c>ClearPool</c> を呼ぶ約束（docs/build.md）になっており、ここに項目を足すと
    /// その呼び出しが黙って空振りして、掴まれたままの DB ファイルが消せなくなる。
    /// </remarks>
    internal static string BuildConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();
    }

    /// <summary>接続を開き、待ちの設定を入れて返す。</summary>
    /// <remarks>
    /// <para>
    /// <c>busy_timeout</c> は<b>接続ごとの状態で、DB ファイルには残らない</b> ──
    /// journal_mode（<see cref="SqliteMessageStore.InitializeAsync"/> が一度だけ WAL にする）とは
    /// そこが違うので、初期化に置けない。プールから返ってきた接続は前回の値を保ったままだが、
    /// 新しく開いた接続と見分ける手段が無いので毎回発行する。
    /// </para>
    /// <para>
    /// <b>PRAGMA を省いて <see cref="SqliteConnection.DefaultTimeout"/> だけにしない。</b>
    /// あちらは Microsoft.Data.Sqlite が SQLITE_BUSY を受け取ってから<b>文ごと投げ直す</b>
    /// 予算で、待ちの刻みが粗い。PRAGMA を入れると SQLite 自身の busy handler が
    /// 呼び出しの中で細かく刻んで待つので、競合 1 回あたりの損が桁で変わる。
    /// 両方を同じ値にしてあるのは、外側の投げ直しが残ると宣言した待ちが何倍にも伸びるため。
    /// </para>
    /// </remarks>
    internal static async Task<SqliteConnection> OpenAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // コマンドは生成時に接続の既定値を写すので、PRAGMA を作る前に入れる。
            connection.DefaultTimeout = (int)BusyTimeout.TotalSeconds;

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA busy_timeout={(int)BusyTimeout.TotalMilliseconds};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch
        {
            // 開けなかった接続を握ったまま例外を投げると、プールへ返らずに滞留する。
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
