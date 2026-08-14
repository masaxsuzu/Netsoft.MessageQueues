using System.Text;
using System.Text.Json;

namespace Netsoft.MessageQueues.Domain;

/// <summary>
/// メッセージのペイロード。有効な JSON で、UTF-8 で <see cref="MaxBytes"/> 以下。
/// </summary>
/// <remarks>
/// <para>
/// 検証はこの型を作る瞬間に済ませる。<see cref="Message"/> や store には
/// 「検証済みのペイロードしか渡ってこない」ので、下流のどこにもサイズ判定や
/// JSON 判定が現れない ── 判定を 2 か所に書くと、上限を変えたときに片方だけ直る。
/// </para>
/// <para>
/// サイズは<b>文字数ではなく UTF-8 のバイト数</b>で測る。永続化されるのも
/// ネットワークに載るのもバイト列で、多バイト文字を含むペイロードは
/// 文字数で測ると上限を静かにすり抜ける。
/// </para>
/// </remarks>
public readonly record struct MessagePayload
{
    /// <summary>ペイロードの上限（UTF-8 バイト数）。64KB。</summary>
    public const int MaxBytes = 64 * 1024;

    private readonly string? _json;

    private MessagePayload(string json) => _json = json;

    /// <summary>
    /// JSON の文字列表現。<c>default</c> から作られた場合は空文字。
    /// </summary>
    public string Json => _json ?? string.Empty;

    /// <summary>
    /// 有効なペイロードを持たない（<c>default</c> のまま）かどうか。
    /// </summary>
    public bool IsEmpty => _json is null;

    /// <summary>
    /// JSON 文字列からペイロードを作る。JSON でないもの・64KB を超えるものは弾く。
    /// </summary>
    /// <exception cref="ArgumentException">値が有効な JSON でないか、64KB を超える場合。</exception>
    public static MessagePayload From(string json) =>
        TryFrom(json, out MessagePayload payload)
            ? payload
            : throw new ArgumentException(
                $"MessagePayload は {MaxBytes} バイト以下の有効な JSON でなければなりません。",
                nameof(json));

    /// <summary>
    /// 例外を投げずにペイロードを作る。外部入力の検証に使う。
    /// </summary>
    /// <remarks>
    /// サイズを JSON 解析より先に見る。上限超えの入力を先に解析すると、
    /// 弾くと決まっているものに解析コストを払うことになる。
    /// </remarks>
    public static bool TryFrom(string? json, out MessagePayload payload)
    {
        payload = default;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        payload = new MessagePayload(json);
        return true;
    }

    public override string ToString() => Json;
}
