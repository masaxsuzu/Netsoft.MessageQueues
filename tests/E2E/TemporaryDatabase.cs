namespace Netsoft.MessageQueues.E2E.Tests;

/// <summary>
/// テスト 1 件ごとの使い捨て DB ファイルの置き場。
/// </summary>
/// <remarks>
/// <b>ブローカーのプロセスとは寿命を分ける。</b>この層には「落として、同じ DB で
/// 立て直す」試験があり、プロセスがファイルまで持つと 1 台目の後始末が
/// 2 台目の DB を消す（実際にそう書いて落ちた）。
/// プロセスはプロセスだけ、ファイルはここだけを持つ。
/// </remarks>
public sealed class TemporaryDatabase : IDisposable
{
    private readonly string _directory;

    public TemporaryDatabase()
    {
        // 並行するテストと衝突しないよう、インスタンスごとに分ける（docs/build.md）。
        _directory = Path.Combine(
            Path.GetTempPath(), "netsoft-messagequeues-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        FilePath = Path.Combine(_directory, "messages.db");
    }

    /// <summary>DB ファイルのパス。</summary>
    public string FilePath { get; }

    public void Dispose()
    {
        // DB を開いているのは別のプロセスで、こちらは接続を持たない。
        // 掴んでいる側は既に殺されているので、プールを閉じる相手も居ない。
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末に失敗してもテストの結果を変えたくない（docs/build.md）。
        }
    }
}
