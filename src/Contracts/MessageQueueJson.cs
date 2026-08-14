using System.Text.Json;

namespace Netsoft.MessageQueues.Contracts;

/// <summary>
/// 線の上の JSON の書き方を、ブローカーと客で 1 つに固定する。
/// </summary>
/// <remarks>
/// 既定の <see cref="JsonSerializerOptions"/> に任せない。あちらは呼ぶ場所ごとに
/// 別の設定を渡せてしまい、片方が camelCase・片方が PascalCase という食い違いを
/// 実行時まで隠す。ASP.NET の既定（<see cref="JsonSerializerDefaults.Web"/>）に
/// そろえてあるので、ブローカー側が素で書いても客が読める。
/// </remarks>
public static class MessageQueueJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
