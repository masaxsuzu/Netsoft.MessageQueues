using Microsoft.Extensions.Hosting;

using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime;

/// <summary>
/// 配送エンジンをホストの寿命に合わせて回す。
/// </summary>
/// <remarks>
/// <para>
/// <b>登録の口は <see cref="MessageQueueServiceCollectionExtensions.AddMessageQueueEngine"/> だけ</b>
/// なので internal にしてある。公開すると「自分で <c>AddHostedService</c> する」経路が並び、
/// 二重に登録された瞬間にエンジンが 2 本走る ── レーンの中が直列という順序の約束が、
/// 配線の書き方ひとつで消える。
/// </para>
/// <para>
/// <b>スキーマの用意（<see cref="IMessageStore.InitializeAsync"/>）は口が開く前に済ませる。</b>
/// 後ろへ回すと、起動直後の発行がテーブルの無い DB に当たる。
/// </para>
/// <para>
/// <b>停止は待つ。</b><see cref="StopAsync"/> でエンジンの完走を待たないと、
/// 処理中の配送が確認を書く前にプロセスが消えうる ── それでも at-least-once は
/// 破れない（未確認は再配送される）が、正常な停止でわざわざ重複を作る理由が無い。
/// </para>
/// </remarks>
internal sealed class DeliveryEngineHostedService : IHostedService
{
    private readonly IMessageStore _store;
    private readonly DeliveryEngine _engine;
    private readonly CancellationTokenSource _stopping = new();

    private Task? _running;

    public DeliveryEngineHostedService(IMessageStore store, DeliveryEngine engine)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(engine);

        _store = store;
        _engine = engine;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _running = _engine.RunAsync(_stopping.Token);
    }

    /// <remarks>
    /// <para>
    /// <b>2 度呼ばれる。</b>ホストの停止と、その後の破棄の両方から来る（テストの
    /// <c>WebApplicationFactory</c> がまさにそうする）。取り消しは冪等で、完了済みの
    /// Task を待つのは何もしないのと同じなので、そのまま素通しでよい。
    /// </para>
    /// <para>
    /// <b>取り消し口を破棄しない。</b>タイマーを持たない
    /// <see cref="CancellationTokenSource"/> は解放の要る資源を握っておらず、
    /// 破棄しても取り消しにはならない。逆に破棄すると 2 つの壊れ方をする ──
    /// 破棄が停止より先に走る経路（<c>WebApplicationFactory</c> はホストを破棄してから
    /// 止める）では次の取り消しが <see cref="ObjectDisposedException"/> になり、
    /// 走っているループがこの token から linked source を作る隙間に当たれば、
    /// <b>停止のために作った口が停止を壊す</b>側になる。
    /// </para>
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_running is null)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);

        // 取り消しは正常完了の契約（DeliveryEngine.RunAsync）なので、例外は握らない。
        await _running.ConfigureAwait(false);
    }
}
