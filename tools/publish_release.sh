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
install_zip="$release_dir/CombatPreview-$version-beta-UMM.zip"
if [ ! -f "$install_zip" ]; then install_zip="$release_dir/CombatPreview-$version-diagnostic-only-UMM.zip"; fi
release_title="v$version — 읽기 전용 미리보기 베타 (기본 주무기 부분 추정)"
if [[ "$install_zip" == *diagnostic-only* ]]; then release_title="v$version — 진단용 개발 빌드 (hover 차단)"; fi
gh release create "v$version" \
  --repo Minki-Neo/WOTR_combat_preview \
  --verify-tag \
  --prerelease \
  --latest=false \
  --title "$release_title" \
  --notes-file "$release_dir/RELEASE_NOTES.md" \
  "$install_zip" \
  "$release_dir/CombatPreview-$version-development-source.zip" \
  "$release_dir/SHA256SUMS.txt"
