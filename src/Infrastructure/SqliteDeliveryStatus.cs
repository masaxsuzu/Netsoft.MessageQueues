using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Infrastructure;

/// <summary>
/// <see cref="DeliveryStatus"/> と保存する文字列の対応を一箇所に固定する。
/// </summary>
/// <remarks>
/// <c>ToString()</c> / <c>Enum.Parse</c> に任せない。あちらは enum の識別子を
/// 改名しただけで保存済みの行が読めなくなる ── 対応表を明示しておけば、
/// 改名はこのファイルの変更として現れ、互換を壊すかどうかをレビューで判断できる。
/// </remarks>
internal static class SqliteDeliveryStatus
{
    public static string ToText(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Pending => "Pending",
        DeliveryStatus.Delivered => "Delivered",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未知の DeliveryStatus。"),
    };

    public static DeliveryStatus FromText(string text) => text switch
    {
        "Pending" => DeliveryStatus.Pending,
        "Delivered" => DeliveryStatus.Delivered,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "未知の DeliveryStatus の文字列。"),
    };
}
