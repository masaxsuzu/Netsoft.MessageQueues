using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Runtime.Remote;

/// <summary>
/// 遠隔の消費者が受け取る 1 件。<see cref="ClaimedDelivery"/> から、外へ出す必要のあるものだけを写した形。
/// </summary>
/// <param name="Message">配送されたメッセージ。</param>
/// <param name="Attempt">何度目の試行か（1 始まり）。</param>
public sealed record RemoteDelivery(Message Message, int Attempt);
