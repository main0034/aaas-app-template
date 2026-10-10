#!/usr/bin/env bash
# Deterministic checks on the change record, run by CI on every pull request.
#
# changes/<id>/ holds one change: brief.md, the request it was built from, and the
# acceptance tests written from that request by a spec-tester who never saw the
# code. The agent harness writes them; the agent that builds the change does not.
#
#   1. A merged change record is never modified or deleted. Its tests are the
#      accepted behaviour; a change that alters that behaviour is a new change,
#      and superseding an old test is a human decision (OQ-24).
#   2. Every commit in the pull request that touches changes/ carries the
#      harness's trailer, `AaaS-Change: <id>`, for exactly the record it touches.
#      A fix round that edits an assertion until CI agrees fails here
#      (FINDINGS.md #25). This stops an honest mistake, not a forged trailer.
#   3. A new change record has brief.md and at least one test file. Between the
#      builder's push and the harness adding the tests this is red, on purpose:
#      a change without its acceptance tests is not mergeable.
#
# Pull requests that add no change record (a human's, a dependency bump) pass.
#
# Usage: scripts/check-changes.sh <base-ref>
set -euo pipefail

base="${1:?usage: check-changes.sh <base-ref>}"
dir="changes"
fail=0

# 1. Merged records are immutable.
while IFS=$'\t' read -r status path _; do
  case "$status" in
    A) ;;
    *)
      echo "::error file=$path::A merged change record changed ($status). changes/ is the accepted behaviour of past changes; it is never edited by a later one. If this change must alter that behaviour, stop and say so - superseding an old acceptance test is a human decision. See AGENT.md."
      fail=1
      ;;
  esac
done < <(git diff --name-status --find-renames "$base"...HEAD -- "$dir")

# 2. Only the harness writes changes/, one record per commit.
while read -r commit; do
  trailer=$(git log -1 --format='%(trailers:key=AaaS-Change,valueonly)' "$commit" | tr -d '[:space:]')
  touched=$(git diff-tree --no-commit-id --name-only -r "$commit" -- "$dir" | cut -d/ -f2 | sort -u | tr '\n' ' ' | sed 's/ $//')
  if [ -z "$trailer" ] || [ "$touched" != "$trailer" ]; then
    echo "::error::Commit ${commit:0:7} changes $dir/$touched without the harness's 'AaaS-Change: $touched' trailer. The change record is written by the agent harness, not by the change: do not edit a request or its acceptance tests. If an acceptance test contradicts the request, say which and stop."
    fail=1
  fi
done < <(git log --no-merges --format=%H "$base"..HEAD -- "$dir")

# 3. A new record is complete.
while read -r id; do
  [ -n "$id" ] || continue
  if [ ! -f "$dir/$id/brief.md" ]; then
    echo "::error file=$dir/$id::Change record $id has no brief.md."
    fail=1
  fi
  if ! ls "$dir/$id/"*.cs >/dev/null 2>&1; then
    echo "::error file=$dir/$id::Change record $id has no acceptance tests yet. The harness adds them after the first push; until then this change is not mergeable."
    fail=1
  fi
done < <(git diff --name-only --diff-filter=A "$base"...HEAD -- "$dir" | cut -d/ -f2 | sort -u)

if [ "$fail" -eq 0 ]; then
  echo "Change records OK: merged records unchanged, new ones complete and written by the harness."
fi
exit "$fail"
