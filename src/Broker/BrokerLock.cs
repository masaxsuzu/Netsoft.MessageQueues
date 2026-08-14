namespace Netsoft.MessageQueues.Broker;

/// <summary>
/// 同じ DB を開くブローカーを 1 つに限る錠。DB の隣の <c>&lt;DB&gt;.lock</c> を排他で開く。
/// </summary>
/// <remarks>
/// <para>
/// <b>2 つ目を立てると壊れるのは、配送ループの本数が増えるから。</b>ループはレーンごとに
/// 1 本という前提で順序を約束している（docs/operating.md）ので、別プロセスが同じレーンを
/// 回し始めると重複配送が常態化し、レーンの中の順序が消える。
/// </para>
/// <para>
/// <b>終了時に消す印で排他してはいけない。</b>プロセスは <c>SIGKILL</c> で死にうる
/// ── そのとき印は残ったままになり、<b>次の正しい起動を永久に拒む</b>。DB に「起動中」の
/// 行を書く方式も、後始末でファイルを消す方式も同じ壊れ方をする。守るはずのものを壊す。
/// </para>
/// <para>
/// <see cref="FileShare.None"/> で開いたファイルハンドルは Unix では <c>flock</c> になり、
/// プロセスが死ねばカーネルが解放する。だから「殺した後なら起動できる」が無条件に成り立つ。
/// この性質は tests/E2E が<b>2 本 1 組</b>で固定している ── 「2 つ目は落ちる」だけでは、
/// 印を書く実装でも通ってしまう。
/// </para>
/// <para>
/// <b>掴めるまで待つ作りにしない。</b>待つと二重起動が「起動が遅い」に見えたまま、
/// 1 つ目が動き続ける限り終わらない（Netsoft.Jobs が同じ判断をしている）。
/// </para>
/// </remarks>
public sealed class BrokerLock : IDisposable
{
    private readonly FileStream _handle;

    private BrokerLock(FileStream handle) => _handle = handle;

    /// <summary>
    /// 錠を掴む。既に別のブローカーが握っていれば例外。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 同じ DB を使うブローカーが既に動いている場合。
    /// </exception>
    public static BrokerLock Acquire(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string path = databasePath + ".lock";

        try
        {
            // 中身は書かない。存在ではなく**掴んでいること**が錠なので、
            // 何が書いてあるかは意味を持たない。消しもしない ── 消す作業は
            // 「印で排他する」方式へ半歩戻ることになる。
            return new BrokerLock(new FileStream(
                path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"同じ DB を使うブローカーが既に動いています（{path} を掴めませんでした）。" +
                "同じ DB に対してブローカーは 1 つだけです。別に立てるなら DB のパスを分けてください。",
                exception);
        }
    }

    public void Dispose() => _handle.Dispose();
}
