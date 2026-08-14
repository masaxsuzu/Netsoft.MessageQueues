namespace Netsoft.MessageQueues.Contracts;

/// <summary>
/// 発行が受け付けられたときの応答。
/// </summary>
/// <remarks>
/// これが返った時点で永続化は完了している（<c>docs/operating.md</c>「発行」）。
/// 配送されたことは意味しない。
/// </remarks>
/// <param name="MessageId">払い出されたメッセージの識別子。</param>
public sealed record PublishedMessage(string MessageId);
