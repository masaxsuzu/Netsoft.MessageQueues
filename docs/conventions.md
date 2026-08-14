# 規約

## コード

- `TreatWarningsAsErrors`。警告を残さない
- ファイルスコープ名前空間を使う
- コード / テスト / コミットログ / コメントの書き分けは
  [CLAUDE.md の「どこに何を書くか」](../CLAUDE.md)。ここには繰り返さない

## 語の使い分け

散文とコードで同じ語を同じ意味に使う。揺れた語は改名で 1 つに戻す。

- **Subscriber（購読者）** — `IMessageSubscriber` を実装する、**利用者が書く側**。
  この基盤にとっての唯一の拡張点
- **Subscription（購読）** — トピックと購読名の組。配送の宛先としてディスクに残る単位。
  購読者はプロセスと共に消えるが、購読は残る ── この区別を崩さない
- **Delivery（配送）** — 1 つのメッセージの、1 つの購読への到達。メッセージそのものと
  混ぜない（メッセージは不変、状態を持つのは配送）
- **`Handler` で終わる型名を付けない** — Netsoft.Jobs では `Handler` が「利用者が書く側」を
  指す語として予約されている。こちらの「利用者が書く側」は Subscriber なので、
  `Handler` という型名が現れたらどちらの意味でも誤り

## 値オブジェクト

外から来る文字列は境界で `From` / `TryFrom` を通し、以後は値オブジェクト
（`MessageId` / `Topic` / `SubscriptionName` / `MessagePayload`）で持ち回る。

- 検証の定義は `TryFrom` に 1 つだけ置き、`From` は委譲する。両方に書くと片方だけ直って受理範囲がずれる
- `readonly record struct` なので `default` が作れてしまう。生の文字列フィールドだけを
  nullable にして受け止め、公開側（`Value`）から null を漏らさない

## 命名とプロジェクト

- プロジェクト名は `Domain` のように短く保つ。アセンブリ名の `Netsoft.MessageQueues.` prefix は
  `src` / `tests` 直下の `Directory.Build.props` が付ける。
  **各 csproj には書かない**（同名 csproj が複数あるため必ず片方がずれる）
- 依存の向きは Runtime → Domain ← Infrastructure の一方通行。
  Runtime と Infrastructure は互いを知らず、配線はホスト（DI の登録）だけが知る
