namespace Netsoft.MessageQueues.Client;

/// <summary>
/// ブローカーへ繋ぐ側の設定。
/// </summary>
public sealed class MessageQueueClientOptions
{
    /// <summary>ブローカーの場所。</summary>
    public Uri BaseAddress { get; set; } = new("http://localhost:5000");

    /// <summary>
    /// 接続が切れた・繋げなかったときに、繋ぎ直すまでの待ち。既定 1 秒。
    /// </summary>
    /// <remarks>
    /// 繋ぎ直しを諦めない。ブローカーの再起動は運用の一部で、そのたびに客の側を
    /// 立て直させる理由が無い。未配送のメッセージはブローカーの DB に残っているので、
    /// 繋がった時点で続きから届く。
    /// </remarks>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);
}
