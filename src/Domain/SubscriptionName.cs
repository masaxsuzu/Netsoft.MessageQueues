namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// 購読の名前。配送の宛先を識別する。
/// </summary>
/// <remarks>
/// 配送の行 (<see cref="Delivery"/>) はこの名前で購読者に紐づく。名前が同じなら
/// プロセスを再起動しても同じ購読として扱われ、未配送分を引き継ぐ ── いわゆる
/// durable subscription。匿名（毎回変わる）購読を作らないのは、名前が変わった瞬間に
/// 未配送の行が誰のものでもなくなり、at-least-once の約束が黙って切れるため。
/// 一意性の単位はトピック内（同じトピックに同じ名前の購読は 1 つ）。
/// </remarks>
public readonly record struct SubscriptionName
{
    private readonly string? _value;

    private SubscriptionName(string value) => _value = value;

    /// <summary>
    /// 購読名の文字列表現。<c>default</c> から作られた場合は空文字。
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// 有効な購読名を持たない（<c>default</c> のまま）かどうか。
    /// </summary>
    public bool IsEmpty => _value is null;

    /// <summary>
    /// 文字列から購読名を作る。空文字・空白のみは弾く。
    /// </summary>
    /// <exception cref="ArgumentException">値が null・空文字・空白のみの場合。</exception>
    public static SubscriptionName From(string value) =>
        TryFrom(value, out SubscriptionName name)
            ? name
            : throw new ArgumentException("SubscriptionName は空にできません。", nameof(value));

    /// <summary>
    /// 例外を投げずに購読名を作る。外部入力の検証に使う。
    /// </summary>
    public static bool TryFrom(string? value, out SubscriptionName name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            name = default;
            return false;
        }

        name = new SubscriptionName(value);
        return true;
    }

    public override string ToString() => Value;
}
