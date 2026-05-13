#!/usr/bin/env python3
"""
branch-sanity.py — PreToolUse hook gating git mutation Bash calls.

Triggers only on `git (push|commit|merge|rebase|reset|switch|checkout|cherry-pick|am)`
calls. For everything else, exits 0 immediately.

Behavior
--------
1. Cooldown: if a block fired within the last 120s in this scope, fast-refuse
   (exit 2). Prevents thrash loops in --dangerously-skip-permissions mode.
2. Escalation: after 10 blocks within a session/scope, write `escalated.flag`
   and refuse all further attempts until the flag is manually removed.
3. Expected-branch contract (optional): if `.claude/state/<scope>/expected-branch`
   exists and the current branch differs, BLOCK with a diagnosis dump.
4. Otherwise: print a single-line informational summary to stderr (current
   branch, dirty count) so the agent has free context, and exit 0.

The hook DOES NOT auto-stash, auto-checkout, or otherwise mutate the repo.
Self-healing is delegated to Claude — it can re-invoke the relevant skill or
take recovery steps based on the diagnosis written to last-diagnosis.md.

Scope key
---------
State is partitioned by `git rev-parse --git-common-dir` (sanitized). Each
worktree's common-dir is unique, so spawned-tab Claudes have isolated state
from the orchestrator's main repo.

Hook protocol
-------------
- stdin: JSON with shape { tool_name, tool_input: { command, ... } }
- exit 0 + stderr: passes through, model sees stderr as context
- exit 2 + stderr: blocks the tool call, model sees stderr as the reason
"""
from __future__ import annotations

import datetime as _dt
import json
import os
import re
import subprocess
import sys
from pathlib import Path

# git mutation verbs we gate. Read-only verbs (status, log, diff, branch, etc.)
# are intentionally omitted to keep token cost minimal.
MUTATION_VERBS = re.compile(
    r"\bgit\s+(push|commit|merge|rebase|reset|switch|checkout|cherry-pick|am)\b"
)

COOLDOWN_SECONDS = 120
MAX_BLOCKS_BEFORE_ESCALATION = 10


def _git(*args: str, cwd: Path | None = None) -> str | None:
    """Run a git command and return stdout, or None on failure."""
    try:
        out = subprocess.run(  # noqa: S603 — controlled args
            ["git", *args],
            cwd=str(cwd) if cwd else None,
            capture_output=True,
            text=True,
            timeout=5,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    if out.returncode != 0:
        return None
    return out.stdout.strip()


def _scope_key(common_dir_abs: str) -> str:
    """Deterministic, filesystem-safe key for the state directory."""
    return re.sub(r"[^A-Za-z0-9_-]+", "_", common_dir_abs).strip("_")[:120] or "default"


def _read_int(path: Path) -> int:
    try:
        return int(path.read_text(encoding="utf-8").strip() or 0)
    except (OSError, ValueError):
        return 0


def _emit_pass(message: str) -> int:
    """Pass-through with informational message to stderr."""
    print(message, file=sys.stderr)
    return 0


def _emit_block(message: str) -> int:
    """Block the tool call. Exit code 2 + stderr is the documented protocol."""
    print(message, file=sys.stderr)
    return 2


def main() -> int:
    # 1. Read hook payload
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        # Malformed input — don't break the agent; just pass.
        return 0

    if payload.get("tool_name") != "Bash":
        return 0

    command = (payload.get("tool_input") or {}).get("command", "") or ""
    if not MUTATION_VERBS.search(command):
        return 0

    # 2. Determine scope (worktree-aware via common-dir)
    common_dir = _git("rev-parse", "--git-common-dir")
    if not common_dir:
        return 0  # not in a git repo — nothing to gate
    common_dir_abs = str(Path(common_dir).resolve())
    scope = _scope_key(common_dir_abs)

    state_dir = Path(".claude") / "state" / scope
    try:
        state_dir.mkdir(parents=True, exist_ok=True)
    except OSError:
        return 0  # cannot write state — bail without breaking the agent

    last_block_file = state_dir / "last-block.txt"
    block_count_file = state_dir / "block-count.txt"
    escalated_flag = state_dir / "escalated.flag"
    expected_branch_file = state_dir / "expected-branch"
    diagnosis_file = state_dir / "last-diagnosis.md"

    now = int(_dt.datetime.now().timestamp())

    # 3. Escalation gate
    if escalated_flag.exists():
        return _emit_block(
            f"branch-sanity: ESCALATED -- {MAX_BLOCKS_BEFORE_ESCALATION} blocks "
            "in this scope. Manual reset required.\n"
            f"  Reset: rm '{escalated_flag}'\n"
            f"  Review: '{diagnosis_file}'"
        )

    # 4. Cooldown gate
    if last_block_file.exists():
        last = _read_int(last_block_file)
        age = now - last
        if 0 <= age < COOLDOWN_SECONDS:
            remaining = COOLDOWN_SECONDS - age
            return _emit_block(
                f"branch-sanity: cooldown active "
                f"(last block {age}s ago, retry in {remaining}s)."
            )

    # 5. Gather diagnosis facts
    current_branch = _git("branch", "--show-current") or "<detached>"
    head_short = _git("rev-parse", "--short", "HEAD") or "<unknown>"
    porcelain = _git("status", "--porcelain") or ""
    dirty_lines = porcelain.splitlines()
    dirty_count = len(dirty_lines)
    status_short = _git("status", "--short") or ""
    status_head = "\n".join(status_short.splitlines()[:5])

    # 6. Expected-branch check (soft contract)
    expected = ""
    if expected_branch_file.exists():
        try:
            expected = expected_branch_file.read_text(encoding="utf-8").strip()
        except OSError:
            expected = ""

    if expected and expected != current_branch:
        # Increment counters, write diagnosis, block.
        count = _read_int(block_count_file) + 1
        try:
            block_count_file.write_text(str(count), encoding="utf-8")
            last_block_file.write_text(str(now), encoding="utf-8")
            iso = _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
            diagnosis = (
                "# branch-sanity diagnosis\n\n"
                f"- Timestamp: {iso}\n"
                f"- Common-dir: {common_dir_abs}\n"
                f"- Expected branch: {expected}\n"
                f"- Actual branch:   {current_branch} ({head_short})\n"
                f"- Dirty files: {dirty_count}\n"
                f"- Block count: {count} / {MAX_BLOCKS_BEFORE_ESCALATION}\n"
                f"- Triggering command: `{command.strip()[:240]}`\n\n"
                "## Status (first 5)\n```\n"
                f"{status_head}\n```\n\n"
                "## Suggested recovery\n"
                f"1. If the expected branch is correct: `git checkout {expected}`\n"
                "2. If you intended this branch, clear the contract: "
                f"`rm '{expected_branch_file}'`\n"
                "3. Re-invoke the running skill (e.g., /stage, /promote) so it "
                "sets the contract correctly\n"
            )
            diagnosis_file.write_text(diagnosis, encoding="utf-8")
        except OSError:
            pass

        if count >= MAX_BLOCKS_BEFORE_ESCALATION:
            try:
                escalated_flag.touch()
            except OSError:
                pass
            return _emit_block(
                f"branch-sanity: ESCALATED after {count} blocks. "
                f"Manual reset required.\n"
                f"  Reset: rm '{escalated_flag}'\n"
                f"  Review: '{diagnosis_file}'"
            )

        return _emit_block(
            f"branch-sanity: BLOCKED -- branch mismatch "
            f"({current_branch} != expected {expected}).\n"
            f"  Block {count} / {MAX_BLOCKS_BEFORE_ESCALATION} this scope.\n"
            f"  See: '{diagnosis_file}'"
        )

    # 7. Pass-through with informational summary
    summary = f"[branch-sanity] {current_branch} ({head_short}), dirty={dirty_count}"
    if dirty_count > 0 and status_head:
        summary += "\n" + "\n".join(
            f"[branch-sanity]   {line}" for line in status_head.splitlines()
        )
    return _emit_pass(summary)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001 — never break the agent over a hook bug
        # Fail-open: if the hook itself errors, don't block.
        sys.exit(0)
