# ビルド・テスト

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes   # 直すときは --verify-no-changes を外す

tools/gate.sh    # 上の 3 つをコミットした木で通し、push を解錠する
```

**.NET 10 SDK が必要**（`global.json` で固定）。ターゲットは `net10.0`、C# 14。

`tools/gate.sh` を通していないコミットは push できない。止めるのは 2 つで、
`tools/gate-hook.sh`（コマンドの実行前。`.claude/settings.json` が掛ける）と
`tools/git-hooks/pre-push`（押す直前。git 自身が呼ぶので迂回できない）。
どうしても素で押すなら `GATE_SKIP=1` を付ける。

**後者は `dotnet build` が入れる**（ルートの `Directory.Build.props`）。git は
リポジトリに置いた設定を読まない ── clone しただけで任意のコードが走るのを防ぐため ──
ので、`core.hooksPath` は clone ごとに設定するしかない。ゲートだけが設定する形だと、
一度もゲートを通していない clone の初回 push が素通りする（Netsoft.Jobs で実際に
起きた穴）。ビルドに乗せると、空白は「一度もビルドしていない clone」まで縮む。
`tools/gate.sh` も従来どおり貼るので、どちらか通った方で入る。

落ちたまま次のタスクへ進まない。

## CI

GitHub Actions（[`.github/workflows/ci.yml`](../.github/workflows/ci.yml)）が
PR と main への push で**上と同じ 3 コマンドを同じ順で**回す。

- **CI は再実行であって、検査の定義ではない。** 定義はこのファイルと `tools/gate.sh` にあり、
  検査を変えるときは gate.sh と workflow とこのファイルを一緒に変える。
  CI にしか無い検査を足すと、手元で通ったのに CI だけ落ちる、が生まれる
- SDK の版は `global.json` から取る（workflow に版を書かない。二重管理になる）
- CI が在っても「手元で通してから push する」規律は変わらない。ゲートはそれを
  仕組みにしたもので、CI は網の 2 枚目

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
