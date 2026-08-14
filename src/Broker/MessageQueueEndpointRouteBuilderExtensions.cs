namespace Netsoft.MessageQueues.Broker;

/// <summary>
/// ブローカーの口をまとめて生やす。
/// </summary>
public static class MessageQueueEndpointRouteBuilderExtensions
{
    /// <summary>発行・配送・確認の口を登録する。</summary>
    public static IEndpointRouteBuilder MapMessageQueue(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        PublishEndpoint.Map(endpoints);
        DeliveryStreamEndpoint.Map(endpoints);
        DeliveryAckEndpoint.Map(endpoints);

        return endpoints;
    }
}
