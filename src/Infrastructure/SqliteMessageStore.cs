using Microsoft.Data.Sqlite;

using Netsoft.MessageQueues.Domain;

namespace Netsoft.MessageQueues.Infrastructure;

/// <summary>
/// <see cref="IMessageStore"/> の SQLite 実装。メッセージ本体と配送行を 2 つのテーブルで持つ。
/// </summary>
/// <remarks>
/// <para>
/// 配送の順序は Messages の <c>Seq</c>（AUTOINCREMENT）で決める。発行時刻の文字列で
/// 並べないのは、同一ミリ秒の発行で順序が不定になるのと、時計が戻ると新しいメッセージが
/// 古いものより先に配られてしまうため。挿入の順そのものを順序の定義にする。
/// </para>
/// <para>
/// ペイロードは受け取った JSON 文字列をそのまま TEXT で保存する。正規化（キーの並び替えや
/// 空白の除去）をしないのは、往復で同じバイト列が返ることが一番単純な約束だから。
/// </para>
/// </remarks>
public sealed class SqliteMessageStore : IMessageStore
{
    private readonly string _connectionString;

    public SqliteMessageStore(string databasePath)
    {
        _connectionString = SqliteConnections.BuildConnectionString(databasePath);
    }

    /// <inheritdoc />
    /// <remarks>
    /// journal_mode=WAL は DB ファイルに残る設定なので、ここで一度だけ発行する
    /// （busy_timeout が接続ごとなのとは対照的 ── <see cref="SqliteConnections.OpenAsync"/>）。
    /// WAL にするのは、読み（監視・テスト）が書き（発行・確認）を待たされないようにするため。
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection =
            await SqliteConnections.OpenAsync(_connectionString, cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS Messages (
                Seq        INTEGER PRIMARY KEY AUTOINCREMENT,
                Id         TEXT NOT NULL UNIQUE,
                Topic      TEXT NOT NULL,
                Payload    TEXT NOT NULL,
                EnqueuedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Deliveries (
                MessageId    TEXT NOT NULL,
                Subscription TEXT NOT NULL,
                Status       TEXT NOT NULL,
                AttemptCount INTEGER NOT NULL,
                PRIMARY KEY (MessageId, Subscription)
            );

            CREATE INDEX IF NOT EXISTS IX_Deliveries_Subscription_Status
                ON Deliveries (Subscription, Status);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AppendAsync(
        Message message,
        IReadOnlyCollection<SubscriptionName> subscriptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(subscriptions);

        if (subscriptions.Count == 0)
        {
            throw new ArgumentException(
                "購読の無いメッセージは永続化できません。配送の約束を果たす相手が居ません。",
                nameof(subscriptions));
        }

        await using SqliteConnection connection =
            await SqliteConnections.OpenAsync(_connectionString, cancellationToken).ConfigureAwait(false);

        // メッセージ本体と配送行は 1 つの取引で書く（IMessageStore の約束 (1)）。
        // 分けると、発行は成功したのに一部の購読だけ配送行が無い中間状態が観測できてしまう。
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand insertMessage = connection.CreateCommand())
        {
            insertMessage.Transaction = transaction;
            insertMessage.CommandText =
                """
                INSERT INTO Messages (Id, Topic, Payload, EnqueuedAt)
                VALUES (@id, @topic, @payload, @enqueuedAt);
                """;
            insertMessage.Parameters.AddWithValue("@id", message.Id.Value);
            insertMessage.Parameters.AddWithValue("@topic", message.Topic.Value);
            insertMessage.Parameters.AddWithValue("@payload", message.Payload.Json);
            insertMessage.Parameters.AddWithValue("@enqueuedAt", SqliteTimestamp.ToText(message.EnqueuedAt));
            await insertMessage.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (SubscriptionName subscription in subscriptions)
        {
            await using SqliteCommand insertDelivery = connection.CreateCommand();
            insertDelivery.Transaction = transaction;
            insertDelivery.CommandText =
                """
                INSERT INTO Deliveries (MessageId, Subscription, Status, AttemptCount)
                VALUES (@messageId, @subscription, @status, 0);
                """;
            insertDelivery.Parameters.AddWithValue("@messageId", message.Id.Value);
            insertDelivery.Parameters.AddWithValue("@subscription", subscription.Value);
            insertDelivery.Parameters.AddWithValue("@status", SqliteDeliveryStatus.ToText(DeliveryStatus.Pending));
            await insertDelivery.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ClaimedDelivery?> TryClaimNextAsync(
        Topic topic,
        SubscriptionName subscription,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection =
            await SqliteConnections.OpenAsync(_connectionString, cancellationToken).ConfigureAwait(false);

        // 選択と試行回数の更新を 1 つの取引にする（IMessageStore の指定）。
        // 配送ループは購読ごとに 1 本（Runtime 側の設計）なので同じ購読で取得が
        // 競ることは無いが、それはこの層が知らなくてよい前提 ── 取引にしておけば
        // 呼び出し側の本数が変わってもここは壊れない。
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string? messageId = null;
        string? payload = null;
        string? enqueuedAt = null;
        int attemptCount = 0;

        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                """
                SELECT m.Id, m.Payload, m.EnqueuedAt, d.AttemptCount
                FROM Deliveries d
                JOIN Messages m ON m.Id = d.MessageId
                WHERE m.Topic = @topic
                  AND d.Subscription = @subscription
                  AND d.Status = @pending
                ORDER BY m.Seq
                LIMIT 1;
                """;
            select.Parameters.AddWithValue("@topic", topic.Value);
            select.Parameters.AddWithValue("@subscription", subscription.Value);
            select.Parameters.AddWithValue("@pending", SqliteDeliveryStatus.ToText(DeliveryStatus.Pending));

            await using SqliteDataReader reader =
                await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messageId = reader.GetString(0);
                payload = reader.GetString(1);
                enqueuedAt = reader.GetString(2);
                attemptCount = reader.GetInt32(3);
            }
        }

        if (messageId is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE Deliveries
                SET AttemptCount = AttemptCount + 1
                WHERE MessageId = @messageId AND Subscription = @subscription;
                """;
            update.Parameters.AddWithValue("@messageId", messageId);
            update.Parameters.AddWithValue("@subscription", subscription.Value);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        Message message = new(
            MessageId.From(messageId),
            topic,
            MessagePayload.From(payload!),
            SqliteTimestamp.FromText(enqueuedAt!));

        return new ClaimedDelivery(message, subscription, attemptCount + 1);
    }

    /// <inheritdoc />
    public async Task MarkDeliveredAsync(
        MessageId messageId,
        SubscriptionName subscription,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection =
            await SqliteConnections.OpenAsync(_connectionString, cancellationToken).ConfigureAwait(false);

        // Pending の行だけを対象にする。既に Delivered なら 0 行更新で終わる ──
        // 再配送と確認が重なる経路で 2 度呼ばれても結果が変わらない（冪等）。
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Deliveries
            SET Status = @delivered
            WHERE MessageId = @messageId AND Subscription = @subscription AND Status = @pending;
            """;
        command.Parameters.AddWithValue("@delivered", SqliteDeliveryStatus.ToText(DeliveryStatus.Delivered));
        command.Parameters.AddWithValue("@messageId", messageId.Value);
        command.Parameters.AddWithValue("@subscription", subscription.Value);
        command.Parameters.AddWithValue("@pending", SqliteDeliveryStatus.ToText(DeliveryStatus.Pending));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Delivery>> GetDeliveriesAsync(
        MessageId messageId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection =
            await SqliteConnections.OpenAsync(_connectionString, cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Subscription, Status, AttemptCount
            FROM Deliveries
            WHERE MessageId = @messageId
            ORDER BY Subscription;
            """;
        command.Parameters.AddWithValue("@messageId", messageId.Value);

        List<Delivery> deliveries = [];
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            deliveries.Add(new Delivery(
                messageId,
                SubscriptionName.From(reader.GetString(0)),
                SqliteDeliveryStatus.FromText(reader.GetString(1)),
                reader.GetInt32(2)));
        }

        return deliveries;
    }
}
