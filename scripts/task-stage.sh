#!/usr/bin/env bash
# Sets a task's Stage on the Pesaroll GitHub project.
#   scripts/task-stage.sh AUTH-01 "Planning"
# Stages: "Not planned", "Planning", "Ready to build", "In progress", "Done"
set -euo pipefail
TASK="${1:?usage: task-stage.sh <TASK-ID> <stage>}"
STAGE="${2:?usage: task-stage.sh <TASK-ID> <stage>}"
OWNER="${PESAROLL_PROJECT_OWNER:-mwangiKibui}"
NUMBER="${PESAROLL_PROJECT_NUMBER:-6}"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
gh project field-list "$NUMBER" --owner "$OWNER" --format json > "$TMP/fields.json"
gh project item-list "$NUMBER" --owner "$OWNER" --limit 500 --format json > "$TMP/items.json"

PROJECT_ID="$(gh project view "$NUMBER" --owner "$OWNER" --format json --jq .id)"
FIELD_ID="$(jq -r '.fields[] | select(.name=="Stage") | .id' "$TMP/fields.json")"
OPTION_ID="$(jq -r --arg s "$STAGE" '.fields[] | select(.name=="Stage") | .options[] | select(.name==$s) | .id' "$TMP/fields.json")"
ITEM_ID="$(jq -r --arg t "[$TASK]" '.items[] | select(.content.title | startswith($t)) | .id' "$TMP/items.json" | head -n1)"

[ -n "$OPTION_ID" ] || { echo "Unknown stage \"$STAGE\"" >&2; exit 1; }
[ -n "$ITEM_ID" ] || { echo "No project item titled [$TASK]" >&2; exit 1; }

gh project item-edit --id "$ITEM_ID" --project-id "$PROJECT_ID" --field-id "$FIELD_ID" \
  --single-select-option-id "$OPTION_ID" >/dev/null
echo "$TASK -> $STAGE"
