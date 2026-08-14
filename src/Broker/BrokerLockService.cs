using Microsoft.Extensions.Options;

namespace Netsoft.MessageQueues.Broker;

/// <summary>
/// 錠を、ホストの一番外側で掴んで離さない常駐。
/// </summary>
/// <remarks>
/// <para>
/// <b>掴むのは <see cref="StartingAsync"/>。</b>ここは全ての常駐の <c>StartAsync</c> より前に
/// 走る唯一の場所で、Kestrel が聴き始めるのも、配送エンジンが回り始めるのも後になる。
/// <c>StartAsync</c> で掴むと、2 つ目のブローカーが<b>ポートを開いてから</b>落ちることになり、
/// 一瞬でも受け付けた発行の行き先が無くなる。
/// </para>
/// <para>
/// 掴めなければ例外を投げてホストごと落とす。それが「2 つ目は立たない」の実体で、
/// 握りつぶして続けると、止めるはずのものが動き出す。
/// </para>
/// </remarks>
public sealed class BrokerLockService : IHostedLifecycleService, IDisposable
{
    private readonly IOptions<BrokerOptions> _options;

    private BrokerLock? _lock;

    public BrokerLockService(IOptions<BrokerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _lock = BrokerLock.Acquire(_options.Value.DatabasePath);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <remarks>
    /// 停止では離さない。離すのは <see cref="Dispose"/> だけ ── <c>StopAsync</c> で離すと、
    /// まだ止まりきっていない配送ループが回っている間に 2 つ目が入れる窓ができる。
    /// 異常終了の場合はカーネルが離すので、こちらの後始末には頼らない。
    /// </remarks>
    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
    }
}
