namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// メッセージの識別子。
/// </summary>
/// <remarks>
/// 採番はこの層の責務ではない。ID の生成方法（GUID か連番か、いつ払い出すか）は
/// 発行側や永続化の都合で決まるため、Domain は「与えられた文字列を識別子として扱う」だけにする。
/// </remarks>
public readonly record struct MessageId
{
    // default(MessageId) が作れてしまう以上、生の文字列は null になりうる。
    // 公開側で null を漏らさないよう、フィールドだけを nullable にして受け止める。
    private readonly string? _value;

    private MessageId(string value) => _value = value;

    /// <summary>
    /// 識別子の文字列表現。<c>default</c> から作られた場合は空文字。
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// 有効な識別子を持たない（<c>default</c> のまま）かどうか。
    /// </summary>
    public bool IsEmpty => _value is null;

    /// <summary>
    /// 文字列から識別子を作る。空文字・空白のみは識別子になりえないので弾く。
    /// </summary>
    /// <remarks>
    /// 何が有効かの定義は <see cref="TryFrom"/> が持ち、こちらは委譲する。
    /// 両方に判定を書くと、定義を変えたときに片方だけ直って受理範囲がずれる。
    /// </remarks>
    /// <exception cref="ArgumentException">値が null・空文字・空白のみの場合。</exception>
    public static MessageId From(string value) =>
        TryFrom(value, out MessageId id)
            ? id
            : throw new ArgumentException("MessageId は空にできません。", nameof(value));

    /// <summary>
    /// 例外を投げずに識別子を作る。外部入力の検証に使う。
    /// </summary>
    public static bool TryFrom(string? value, out MessageId id)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            id = default;
            return false;
        }

        id = new MessageId(value);
        return true;
    }

    public override string ToString() => Value;
}
