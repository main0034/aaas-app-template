#!/usr/bin/env bash
# Deterministic checks on schema migrations, run by CI on every pull request.
#
#   1. An existing migration is never modified, renamed or deleted. Once merged it
#      may already have run against a customer's database; editing it changes
#      history without changing the database, and the two silently diverge.
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

# 1. Immutable history. The model snapshot is regenerated on every
#    `migrations add`, so it is the one file allowed to change.
while IFS=$'\t' read -r status path _; do
  [ "$path" = "$snapshot" ] && continue
  case "$status" in
    A) ;;
    *)
      echo "::error file=$path::Existing migration changed ($status). Migrations are immutable once merged - add a new one instead. See AGENT.md."
      fail=1
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
  echo "Migrations OK: history unchanged, new migrations are expand-only."
fi
exit "$fail"
