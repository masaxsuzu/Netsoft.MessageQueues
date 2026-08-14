# ビルド・テスト

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes   # 直すときは --verify-no-changes を外す
```

**.NET 10 SDK が必要**（`global.json` で固定）。ターゲットは `net10.0`、C# 14。

3 つすべてをコミットした木で通してから push する。落ちたまま次のタスクへ進まない。

## テストの後始末

一時ファイルを使うテストはこの形にそろえる。同じ注意書きを各ファイルに写さず、ここを参照する。

- **一時ディレクトリはインスタンスごとに分ける**（`Path.GetRandomFileName()`）。
  xUnit はクラスをまたいで並行に走るので、固定名だと別のテストと同じファイルを掴む
- **SQLite のプールは自分の DB だけ閉じる**。`SqliteConnection.ClearPool(自分の接続文字列)` を使う。
  `ClearAllPools` は<b>プロセス全域</b>に効き、並行して走っている他のテストが使用中の接続まで
  破棄して `ObjectDisposedException` のフレークを起こす（Netsoft.Jobs で実際に起きた）。
  接続文字列は store 側と同じ組み立て（`DataSource` のみ）にする。違うと別のプールに当たって効かない
- プールを閉じるのは削除の前。接続を握ったままだとファイルが開いたままで、Windows では削除に失敗する
- **後始末の失敗でテストの結果を変えない**。`IOException` は握りつぶす。一時ディレクトリはいずれ OS が回収する

## テストの置き場

- `tests/Domain` — 値の検証（ペイロードの上限・JSON 判定・識別子）。DB もスレッドも触らない
- `tests/Infrastructure` — SQL が本当に正しいか、プロセスをまたいで残るか。実ファイルの SQLite で確かめる
- `tests/Runtime` — 配送の意味論（at-least-once・順序・複数購読）。エンジンを実際に走らせ、
  実ファイルの SQLite と組で確かめる ── フェイクの store で通しても「本物でも残る」ことの証明にならない
