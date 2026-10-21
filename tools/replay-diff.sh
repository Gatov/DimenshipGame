#!/usr/bin/env bash
# Runs every replay script under every policy twice, once with the harness built from a base ref
# and once with the harness built from the working tree, and diffs the two reports.
#
# This is the check a refactor is held to: the same content and scripts, two builds of the code,
# and identical reports, final-state hash included. Nothing is pinned. The base is whatever you
# name, so the logic stays free to change in commits that say they change it, and a refactor is
# compared only with the commit it starts from.
#
# Both builds read the working tree's content and scripts. Changing those is a change to the
# inputs, not to the code, so a refactor should leave them alone.
#
# usage: tools/replay-diff.sh [base-ref]     (default: master)
# exit:  0 every report identical, 1 a report differs or a replay failed, 2 the base or a build failed

set -euo pipefail

base_ref="${1:-master}"
repo="$(git rev-parse --show-toplevel)"
work="$(mktemp -d)"
trap 'git -C "$repo" worktree remove --force "$work/base" >/dev/null 2>&1 || true; rm -rf "$work"' EXIT

if ! git -C "$repo" worktree add --detach "$work/base" "$base_ref" >/dev/null 2>&1; then
    echo "cannot check out '$base_ref' as the base" >&2
    exit 2
fi

build() {
    if ! dotnet build "$1/tools/Dimenship.Replay" -c Release -o "$2" -nologo -v quiet >"$2.log" 2>&1; then
        echo "build failed for $3; see below" >&2
        tail -20 "$2.log" >&2
        exit 2
    fi
}

build "$work/base" "$work/bin-base" "$base_ref"
build "$repo" "$work/bin-head" "the working tree"

content="$repo/dimenship/content"
policies=(queue-order replenishment improved)
same=0
differ=0
failed=0

for script in "$repo"/tools/Dimenship.Replay/scripts/*.json; do
    name="$(basename "$script" .json)"
    for policy in "${policies[@]}"; do
        base_status=0
        head_status=0
        dotnet "$work/bin-base/Dimenship.Replay.dll" "$content" "$script" "$policy" >"$work/$name.$policy.base" 2>&1 || base_status=$?
        dotnet "$work/bin-head/Dimenship.Replay.dll" "$content" "$script" "$policy" >"$work/$name.$policy.head" 2>&1 || head_status=$?

        # Two runs that fail the same way would compare equal and prove nothing.
        if [ "$base_status" -ne 0 ] || [ "$head_status" -ne 0 ]; then
            failed=$((failed + 1))
            echo "FAILED   $name ($policy): exit $base_status on $base_ref, $head_status on the working tree"
            tail -5 "$work/$name.$policy.head"
            continue
        fi

        if cmp -s "$work/$name.$policy.base" "$work/$name.$policy.head"; then
            same=$((same + 1))
        else
            differ=$((differ + 1))
            echo "DIFFERS  $name ($policy)"
            diff -u --label "$base_ref" --label "working tree" \
                "$work/$name.$policy.base" "$work/$name.$policy.head" | head -40 || true
        fi
    done
done

echo "replay-diff against $base_ref: $same identical, $differ differ, $failed failed to run"
[ "$differ" -eq 0 ] && [ "$failed" -eq 0 ]
