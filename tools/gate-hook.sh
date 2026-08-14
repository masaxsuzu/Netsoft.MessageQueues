#!/usr/bin/env bash
#
# PreToolUse(Bash)。ゲートを通した木からの push だけを許す。
#
# 判定に使うのは「tools/gate.sh が押したマーカー」であって「手順を踏んだか」ではない。
# 踏んだかどうかは検証できないし、検証したいのは手順ではなく
# ビルドとテストと整形が通っていることだから。
#
# 止めるのは push だけにしてある。commit を止めると、直しかけを保存する手も塞がる。
#
set -uo pipefail

command=$(jq -r '.tool_input.command // ""' 2>/dev/null) || exit 0

# `git push` を「文字列として含むか」で見てはいけない。それだと push について
# 書いてあるだけのコマンド（echo、grep、ヒアドキュメント）まで止まる
# （Netsoft.Jobs でこのフックを書いた回に、テスト用の JSON を echo しただけで止められた）。
#
# 行頭か区切りの直後に来る git で、その最初の非オプション語が push のものだけを見る。
# `git -C <path> push` も拾えるよう、オプションは値を 1 つ取れることにしてある。
#
# それでも完全ではない。grep は行単位なので、複数行のコマンドの中に
# `git push` で始まる行や `&& git push` を含む行があれば当たる。
# **多めに止まる方に倒してある。** 見逃すと壊れたものが main へ向かうが、
# 止まりすぎても逃げ道を使えば済む。
git_push='(^|[;&|]|&&|\|\|)[[:space:]]*git([[:space:]]+-[^[:space:]]+([[:space:]]+[^-][^[:space:]]*)?)*[[:space:]]+push([[:space:]]|$)'

printf '%s' "$command" | grep -Eq "$git_push" || exit 0

# 逃げ道は環境変数ではなくコマンドの中身で見る。フックは push を走らせる shell の
# 親ではなく別プロセスなので、`GATE_SKIP=1 git push` と書かれても
# その変数はフックに届かない（Netsoft.Jobs がそう書いて動かず気づいた）。届くのは文字列の方。
printf '%s' "$command" | grep -q 'GATE_SKIP=1' && exit 0

root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$root" || exit 0

tree=$(git rev-parse 'HEAD^{tree}' 2>/dev/null) || exit 0
marker="$(git rev-parse --git-dir)/gate-ok"

if [ -f "$marker" ] && [ "$(cat "$marker")" = "$tree" ]; then
    exit 0
fi

cat >&2 <<'MSG'
このコミットはゲートを通っていないので push を止めました。

    tools/gate.sh          # build / test / format を通してマーカーを押す

素で押す必要があるなら GATE_SKIP=1 を付けてください。
MSG
exit 2
