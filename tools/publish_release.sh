#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export GH_PROMPT_DISABLED=1
export GH_NO_UPDATE_NOTIFIER=1
version=${1:-$(python3 -c 'import json; print(json.load(open("CombatPreview/Info.json"))["Version"])')}
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo 'Expected a numeric major.minor.patch version' >&2
  exit 2
fi
release_dir="releases/v$version"
gh api repos/Minki-Neo/WOTR_combat_preview --jq '{private,default_branch}'
(
  cd "$release_dir"
  sha256sum -c SHA256SUMS.txt
)
gh release create "v$version" \
  --repo Minki-Neo/WOTR_combat_preview \
  --verify-tag \
  --prerelease \
  --latest=false \
  --title "v$version — 진단용 개발 빌드 (hover 미리보기 차단)" \
  --notes-file "$release_dir/RELEASE_NOTES.md" \
  "$release_dir/CombatPreview-$version-diagnostic-only-UMM.zip" \
  "$release_dir/CombatPreview-$version-development-source.zip" \
  "$release_dir/SHA256SUMS.txt"
