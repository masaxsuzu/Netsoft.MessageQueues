namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// メッセージの宛先を表すトピック名。発行者はトピックへ発行し、購読はトピック単位で結ぶ。
/// </summary>
/// <remarks>
/// 階層やワイルドカードの意味付けはしない。この基盤の購読はプロセス内で
/// 静的に決まる（<c>docs/operating.md</c>）ので、名前の一致だけで足りる。
/// パターン照合を持ち込むと、どの購読に配るかが発行のたびの解釈になり、
/// 配送の行き先を永続化の時点で確定できなくなる。
/// </remarks>
public readonly record struct Topic
{
    private readonly string? _value;

    private Topic(string value) => _value = value;

    /// <summary>
    /// トピック名の文字列表現。<c>default</c> から作られた場合は空文字。
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// 有効なトピック名を持たない（<c>default</c> のまま）かどうか。
    /// </summary>
    public bool IsEmpty => _value is null;

    /// <summary>
    /// 文字列からトピック名を作る。空文字・空白のみは弾く。
    /// </summary>
    /// <exception cref="ArgumentException">値が null・空文字・空白のみの場合。</exception>
    public static Topic From(string value) =>
        TryFrom(value, out Topic topic)
            ? topic
            : throw new ArgumentException("Topic は空にできません。", nameof(value));

    /// <summary>
    /// 例外を投げずにトピック名を作る。外部入力の検証に使う。
    /// </summary>
    public static bool TryFrom(string? value, out Topic topic)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            topic = default;
            return false;
        }

        topic = new Topic(value);
        return true;
    }

    public override string ToString() => Value;
}
