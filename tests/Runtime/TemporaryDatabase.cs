using Microsoft.Data.Sqlite;

using Netsoft.MessageQueues.Infrastructure;

namespace Netsoft.MessageQueues.Runtime.Tests;

/// <summary>
/// テスト 1 件ごとの使い捨て DB ファイル。tests/Infrastructure の同名クラスと同じ形。
/// </summary>
/// <remarks>
/// 共有プロジェクトに括らず各テストプロジェクトが持つ（Netsoft.Jobs と同じ判断）。
/// 括ると 3 つ目のプロジェクトが増え、この数十行のために依存の線が 1 本増える。
/// </remarks>
public sealed class TemporaryDatabase : IDisposable
{
    private readonly string _directory;

    public TemporaryDatabase()
    {
        // 並行するテストと衝突しないよう、インスタンスごとに分ける（docs/build.md）。
        _directory = Path.Combine(Path.GetTempPath(), "netsoft-messagequeues-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        FilePath = Path.Combine(_directory, "messages.db");
    }

    /// <summary>DB ファイルのパス。</summary>
    public string FilePath { get; }

    /// <summary>初期化済みのストアを新しく開く。同じファイルを別インスタンスから触れる。</summary>
    public async Task<SqliteMessageStore> OpenStoreAsync()
    {
        SqliteMessageStore store = new(FilePath);
        await store.InitializeAsync(CancellationToken.None);
        return store;
    }

    public void Dispose()
    {
        // プールを閉じてから消す。閉じるのは自分の DB のプールだけ
        // （理由は docs/build.md「テストの後始末」）。
        using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder { DataSource = FilePath }.ToString());
        SqliteConnection.ClearPool(connection);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末に失敗してもテストの結果を変えたくない。
            // 一時ディレクトリなので、残ってもいずれ OS が回収する。
        }
    }
}
