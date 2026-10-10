#!/usr/bin/env bash
# Deterministic checks on schema migrations, run by CI on every pull request.
#
#   1. A migration that has been applied is never modified, renamed or deleted:
#      editing it changes history without changing the database, and the two
#      silently diverge. "Applied" is read from the estate's record,
#      aaas-deployments/<deployment_path>/applied.json, which the apply pipeline
#      writes after a green readiness gate (OQ-21). A migration merged but newer
#      than the estate's applied one never reached a database and may be fixed
#      forward. Without a record (never deployed since records began, or the
#      record cannot be read) every merged migration is treated as applied.
#   2. A new migration does not drop or rename a table or column. Rollback means
#      redeploying the previous image, and the previous image still reads those
#      columns. Destructive changes need a human, not an agent.
#
# Usage: scripts/check-migrations.sh <base-ref>
set -euo pipefail

base="${1:?usage: check-migrations.sh <base-ref>}"
dir="src/App/Migrations"
snapshot="$dir/AppDbContextModelSnapshot.cs"
fail=0

# What the estate this repo feeds has applied: the migration id /ready reported.
applied=""
deployment_path=$(grep -E '^deployment_path=' .aaas/deployment 2>/dev/null | cut -d= -f2- | tr -d '[:space:]' || true)
record_url="https://raw.githubusercontent.com/${AAAS_DEPLOYMENTS_REPO:-main0034/aaas-deployments}/master/${deployment_path}/applied.json"
# AAAS_APPLIED_RECORD: a local file instead, for testing this script.
if [ -n "${AAAS_APPLIED_RECORD:-}" ]; then
  record_url="$AAAS_APPLIED_RECORD"
  record=$(cat "$AAAS_APPLIED_RECORD" 2>/dev/null || true)
  applied=$(jq -r '.migration // empty' <<<"${record:-{\}}" | grep -Eo '^[0-9]{14}' || true)
elif [ -n "$deployment_path" ] && record=$(curl -fsSL --max-time 20 "$record_url" 2>/dev/null); then
  applied=$(jq -r '.migration // empty' <<<"$record" | grep -Eo '^[0-9]{14}' || true)
fi
if [ -n "$applied" ]; then
  echo "Estate ${deployment_path} has applied migrations up to ${applied}."
else
  echo "No applied record for '${deployment_path:-?}' (${record_url}): treating every merged migration as applied."
fi

# 1. Immutable once applied. The model snapshot is regenerated on every
#    `migrations add`, so it is the one file allowed to change.
while IFS=$'\t' read -r status path _; do
  [ "$path" = "$snapshot" ] && continue
  case "$status" in
    A) ;;
    *)
      id=$(basename "$path" | grep -Eo '^[0-9]{14}' || true)
      if [ -n "$applied" ] && [ -n "$id" ] && [ "$id" -gt "$applied" ]; then
        echo "::notice file=$path::Merged migration changed ($status), allowed: it is newer than ${applied}, the last one ${deployment_path} applied."
      else
        echo "::error file=$path::Applied migration changed ($status). It has run against the database (${deployment_path} is at ${applied:-unknown}); add a new migration instead. See AGENT.md."
        fail=1
      fi
      ;;
  esac
done < <(git diff --name-status --find-renames "$base"...HEAD -- "$dir")

# 2. Expand-only. Only Up() is checked: every migration's Down() drops what
#    Up() created, and that is expected.
destructive='migrationBuilder\.(DropTable|DropColumn|RenameTable|RenameColumn)\('
while read -r path; do
  case "$path" in
    *.Designer.cs|"$snapshot") continue ;;
  esac
  hits=$(awk '/override void Up\(/{f=1} /override void Down\(/{f=0} f' "$path" \
    | grep -Eo "$destructive" || true)
  if [ -n "$hits" ]; then
    echo "::error file=$path::Destructive migration ($(echo "$hits" | tr '\n' ' ')). Rollback would break: the previous image still uses what this removes. Needs a human decision - see AGENT.md."
    fail=1
  fi
done < <(git diff --name-only --diff-filter=A "$base"...HEAD -- "$dir")

if [ "$fail" -eq 0 ]; then
  echo "Migrations OK: applied history unchanged, new migrations are expand-only."
fi
exit "$fail"
