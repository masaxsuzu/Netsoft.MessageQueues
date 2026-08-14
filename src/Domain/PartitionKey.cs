namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// パーティションキー。同じキーのメッセージは同じレーンへ落ち、発行順に配送される。
/// </summary>
/// <remarks>
/// 順序の約束の単位。注文 ID のように「この単位の中では順序が要る」ものをキーにする。
/// キーは任意で、渡さなければメッセージは自分の識別子でレーンが決まる
/// （<see cref="Message.PartitionHash"/>）── 互いの順序は約束されない。
/// </remarks>
public readonly record struct PartitionKey
{
    private readonly string? _value;

    private PartitionKey(string value) => _value = value;

    /// <summary>
    /// キーの文字列表現。<c>default</c> から作られた場合は空文字。
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// 有効なキーを持たない（<c>default</c> のまま）かどうか。
    /// </summary>
    public bool IsEmpty => _value is null;

    /// <summary>
    /// レーンの割り当てに使う、プロセスをまたいで安定なハッシュ。非負。
    /// </summary>
    /// <remarks>
    /// 空のキーで読まないこと。キーの無いメッセージのレーンは
    /// <see cref="Message.PartitionHash"/> が識別子から決める。
    /// </remarks>
    public long Hash => StableHash.OfUtf8(Value);

    /// <summary>
    /// 文字列からキーを作る。空文字・空白のみは弾く。
    /// </summary>
    /// <exception cref="ArgumentException">値が null・空文字・空白のみの場合。</exception>
    public static PartitionKey From(string value) =>
        TryFrom(value, out PartitionKey key)
            ? key
            : throw new ArgumentException("PartitionKey は空にできません。", nameof(value));

    /// <summary>
    /// 例外を投げずにキーを作る。外部入力の検証に使う。
    /// </summary>
    public static bool TryFrom(string? value, out PartitionKey key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key = default;
            return false;
        }

        key = new PartitionKey(value);
        return true;
    }

    public override string ToString() => Value;
}
