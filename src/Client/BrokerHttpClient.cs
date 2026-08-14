namespace Netsoft.MessageQueues.Client;

/// <summary>
/// ブローカーへ話しかけるための <see cref="HttpClient"/> を 1 つ抱える。
/// </summary>
/// <remarks>
/// <para>
/// <b>待ち時間を無限にしてある。</b>既定の 100 秒は「要求が終わるまで」の予算で、
/// 応答の本体を読んでいる間も進む ── 配送の流れ（SSE）は届くまで何も来ないのが
/// 普通なので、既定のままだと 100 秒ごとに接続が切られる。切れても再配送されるだけで
/// 壊れはしないが、静かなトピックほど無駄な繋ぎ直しを繰り返すことになる。
/// 呼び出しごとの打ち切りは <see cref="CancellationToken"/> の仕事。
/// </para>
/// <para>
/// 素の <see cref="HttpClient"/> を DI へ直接置かないのは、利用者の容器に
/// 「誰の物でもない HttpClient」を生やさないため。
/// </para>
/// </remarks>
public sealed class BrokerHttpClient : IDisposable
{
    public BrokerHttpClient(MessageQueueClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Http = new HttpClient
        {
            BaseAddress = options.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>ブローカーへの口。</summary>
    public HttpClient Http { get; }

    public void Dispose() => Http.Dispose();
}
