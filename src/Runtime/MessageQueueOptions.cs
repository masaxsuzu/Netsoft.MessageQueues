namespace Netsoft.MessageQueues.Runtime;

/// <summary>
/// 配送の設定。
/// </summary>
public sealed class MessageQueueOptions
{
    /// <summary>
    /// 購読者が例外を投げたあと、同じメッセージを再配送するまでの待ち。既定 5 秒。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 待ち無しで再配送しない。失敗の原因が外部（依存先の停止など）にあるとき、
    /// 即時の再試行はほぼ確実にまた失敗し、失敗ログでディスクを埋めながら CPU を回すだけになる。
    /// </para>
    /// <para>
    /// 上限回数は持たない ── 尽きたときの行き先（破棄か死蔵か）を決めることになり、
    /// 破棄は at-least-once の約束を破る。詰まった購読は
    /// <see cref="Domain.IMessageStore.GetDeliveriesAsync"/> の試行回数で観測できる。
    /// dead letter が要るなら、それは購読者の側で「何度目かを見て退避して正常に返す」
    /// ことで実現できる（attempt が渡っているのはこのため）。
    /// </para>
    /// </remarks>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
