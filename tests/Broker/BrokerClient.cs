using System.Net.Http.Json;
using System.Text;

using Netsoft.MessageQueues.Contracts;

namespace Netsoft.MessageQueues.Broker.Tests;

/// <summary>
/// 口を叩くときの決まり文句をまとめたもの。テストが確かめたいのは応答なので、
/// 組み立ての繰り返しはここへ寄せる。
/// </summary>
public static class BrokerClient
{
    /// <summary>ペイロードをそのまま本体にして発行する。</summary>
    public static Task<HttpResponseMessage> PublishAsync(
        this HttpClient client,
        string topic,
        string payload,
        string? key = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        // 本体はバイト列そのもの。JSON へ包み直さないことが往復の同一性を担保している。
        StringContent content = new(payload, Encoding.UTF8, "application/json");
        return client.PostAsync(MessageQueueRoutes.Publish(topic, key), content);
    }

    /// <summary>発行が受け付けられたことを確かめ、払い出された識別子を返す。</summary>
    public static async Task<string> PublishOkAsync(
        this HttpClient client,
        string topic,
        string payload,
        string? key = null)
    {
        using HttpResponseMessage response = await client.PublishAsync(topic, payload, key);
        response.EnsureSuccessStatusCode();

        PublishedMessage? published = await response.Content.ReadFromJsonAsync<PublishedMessage>();
        return published!.MessageId;
    }

    public static Task<HttpResponseMessage> AckAsync(
        this HttpClient client,
        string topic,
        string subscription,
        int lane,
        string messageId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.PostAsync(MessageQueueRoutes.Ack(topic, subscription, lane, messageId), content: null);
    }

    public static Task<HttpResponseMessage> NackAsync(
        this HttpClient client,
        string topic,
        string subscription,
        int lane,
        string messageId,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.PostAsync(
            MessageQueueRoutes.Nack(topic, subscription, lane, messageId, reason), content: null);
    }
}
