namespace Netsoft.MessageQueues.Contracts;

/// <summary>
/// 配送 1 件ぶんの、線の上での姿。SSE の data として 1 行で流れる。
/// </summary>
/// <remarks>
/// <b><see cref="Payload"/> は JSON を<i>文字列として</i>包む。</b>JSON をそのまま
/// 埋め込む形（生値）にしない ── ペイロードは整形済み（改行を含む）でもよいのに対し、
/// SSE の data は行で区切られるので、生の改行が混ざるとそこで枠が壊れる。
/// 文字列に包めば改行は <c>\n</c> へ逃がされ、受け取り側が取り出したときに
/// <b>発行されたバイト列がそのまま戻る</b>（この基盤の「往復で同じバイト列」の約束）。
/// </remarks>
/// <param name="MessageId">メッセージの識別子。確認のときにそのまま返す。</param>
/// <param name="Topic">配送元のトピック。</param>
/// <param name="Key">パーティションキー。付いていなければ null。</param>
/// <param name="Attempt">何度目の試行か（1 始まり）。</param>
/// <param name="Payload">ペイロードの JSON。</param>
public sealed record DeliveryEvent(
    string MessageId,
    string Topic,
    string? Key,
    int Attempt,
    string Payload);
