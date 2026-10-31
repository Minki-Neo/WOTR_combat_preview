#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export GH_PROMPT_DISABLED=1
export GH_NO_UPDATE_NOTIFIER=1
gh api repos/Minki-Neo/WOTR_combat_preview --jq '{private,default_branch}'
(
  cd releases/v0.2.1
  sha256sum -c SHA256SUMS.txt
)
gh release create v0.2.1 \
  --repo Minki-Neo/WOTR_combat_preview \
  --verify-tag \
  --prerelease \
  --latest=false \
  --title 'v0.2.1 — 진단용 개발 빌드 (hover 미리보기 차단)' \
  --notes-file releases/v0.2.1/RELEASE_NOTES.md \
  releases/v0.2.1/CombatPreview-0.2.1-diagnostic-only-UMM.zip \
  releases/v0.2.1/CombatPreview-0.2.1-development-source.zip \
  releases/v0.2.1/SHA256SUMS.txt
