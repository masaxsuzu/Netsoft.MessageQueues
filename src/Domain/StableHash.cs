using System.Text;

namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// プロセスをまたいで安定な文字列ハッシュ。レーンの割り当てに使う。
/// </summary>
/// <remarks>
/// <see cref="string.GetHashCode()"/> を使わない。あれはプロセスごとに乱数化されるので、
/// 永続化した値と突き合わせた瞬間に「同じキーが再起動後は別のレーンへ落ちる」──
/// 同じキーの順序を守るという約束が、再起動 1 回で黙って壊れる。
/// FNV-1a（64bit）は定義が固定で、実装がこの十数行に収まる。
/// </remarks>
internal static class StableHash
{
    internal static long OfUtf8(string value)
    {
        ulong hash = 14695981039346656037;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash = (hash ^ b) * 1099511628211;
        }

        // SQLite の % は負の被除数に負の余りを返すので、符号を落として非負に固定する。
        return (long)(hash & 0x7FFF_FFFF_FFFF_FFFF);
    }
}
