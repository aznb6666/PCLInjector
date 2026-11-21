#!/usr/bin/env bash
# PCL 同款：framework-dependent 单文件发布。
# 用法：scripts/publish-singlefile.sh [输出目录]
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/publish}"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
RID=win-x64

rm -rf "$OUT"
mkdir -p "$OUT"

"$DOTNET" publish "$ROOT/PCLInjector.csproj" \
  -c Release -r "$RID" --self-contained false -o "$OUT"

# 与 PCL 官方脚本一致：发布产物不保留 PDB
find "$OUT" -maxdepth 1 -name '*.pdb' -delete

echo "published: $OUT"
ls -lh "$OUT"
