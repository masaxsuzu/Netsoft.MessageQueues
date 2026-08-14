namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// 購読の中の直列の単位。全レーン数と、その中の自分の位置。
/// </summary>
/// <remarks>
/// メッセージは <see cref="Message.PartitionHash"/> をレーン数で割った余りでレーンへ落ちる。
/// 同じキーは同じレーンなので、レーンの中の直列がそのまま「同じキーの中は発行順」になる。
/// </remarks>
public readonly record struct Lane
{
    private readonly int _index;

    // 0 を「1 本」と読むことで default(Lane) を Single と同じにする。
    // 生の Count を持つと default が Count 0 になり、割り当ての除算が使う側で壊れる。
    private readonly int _countAboveOne;

    /// <exception cref="ArgumentOutOfRangeException">
    /// レーン数が 1 未満、または位置がレーン数の範囲外の場合。
    /// </exception>
    public Lane(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);

        _index = index;
        _countAboveOne = count - 1;
    }

    /// <summary>このレーンの位置（0 始まり）。</summary>
    public int Index => _index;

    /// <summary>全レーン数。</summary>
    public int Count => _countAboveOne + 1;

    /// <summary>レーンが 1 本だけの構成。購読全体が発行順になる。</summary>
    public static Lane Single => default;

    /// <summary>ハッシュがどのレーンへ落ちるかを返す。</summary>
    public static int IndexFor(long partitionHash, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(partitionHash);

        return (int)(partitionHash % count);
    }

    /// <summary>指定されたハッシュがこのレーンへ落ちるかどうか。</summary>
    public bool Contains(long partitionHash) => IndexFor(partitionHash, Count) == Index;

    public override string ToString() => $"{Index}/{Count}";
}
