using System.Net;
using System.Text;
using System.Text.Json;

using Netsoft.MessageQueues.Contracts;
using Netsoft.MessageQueues.Domain;
using Netsoft.MessageQueues.Runtime;

namespace Netsoft.MessageQueues.Client;

/// <summary>
/// ブローカー越しに発行する <see cref="IMessagePublisher"/>。
/// </summary>
/// <remarks>
/// <b>プロセス内の実装と同じ口・同じ例外にしてある。</b>購読者の居ないトピックへの
/// 発行は、あちらでは <see cref="InvalidOperationException"/>、こちらでは 409 で返る
/// ── それを同じ例外へ翻訳しておけば、発行する側のコードは処理がどこで走るかを
/// 知らずに済む。ペイロードの検証は <see cref="MessagePayload"/> を作る時点で
/// 済んでいるので、線に載せる前に落ちる（往復してから 400 で知る、にならない）。
/// </remarks>
public sealed class HttpMessagePublisher : IMessagePublisher
{
    private readonly BrokerHttpClient _broker;

    public HttpMessagePublisher(BrokerHttpClient broker)
    {
        ArgumentNullException.ThrowIfNull(broker);

        _broker = broker;
    }

    /// <inheritdoc />
    public Task<MessageId> PublishAsync(
        Topic topic,
        MessagePayload payload,
        CancellationToken cancellationToken) =>
        PublishAsync(topic, payload, default, cancellationToken);

    /// <inheritdoc />
    public async Task<MessageId> PublishAsync(
        Topic topic,
        MessagePayload payload,
        PartitionKey key,
        CancellationToken cancellationToken)
    {
        // 本体はペイロードそのもの。包み直さないので、発行したバイト列がそのまま届く。
        using StringContent content = new(payload.Json, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _broker.Http
            .PostAsync(
                MessageQueueRoutes.Publish(topic.Value, key.IsEmpty ? null : key.Value),
                content,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                $"トピック {topic} に購読が宣言されていません。ブローカーの設定を確認してください。");
        }

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        PublishedMessage? published = JsonSerializer.Deserialize<PublishedMessage>(json, MessageQueueJson.Options);

        return MessageId.From(published!.MessageId);
    }
}
