#!/usr/bin/env bash
# fnl-cat custom statusline for Claude Code.
# Reads session JSON on stdin, prints a colorized two-line status on stdout.

INPUT=$(cat)

get_json() {
  local path="$1"
  if command -v jq >/dev/null 2>&1; then
    printf '%s' "$INPUT" | jq -r "$path // empty" 2>/dev/null
    return
  fi
  local key="${path##*.}"
  printf '%s' "$INPUT" \
    | grep -oE "\"${key}\"[[:space:]]*:[[:space:]]*(\"[^\"]*\"|-?[0-9]+(\.[0-9]+)?)" \
    | head -1 \
    | sed -E 's/^[^:]+:[[:space:]]*//; s/^"//; s/"$//'
}

CWD=$(get_json '.workspace.current_dir')
[ -z "$CWD" ] && CWD=$(get_json '.cwd')
[ -z "$CWD" ] && CWD="$PWD"

MODEL_NAME=$(get_json '.model.display_name')
MODEL_ID=$(get_json '.model.id')
[ -z "$MODEL_NAME" ] && MODEL_NAME="$MODEL_ID"

COST=$(get_json '.cost.total_cost_usd')
TRANSCRIPT=$(get_json '.transcript_path')

cd "$CWD" 2>/dev/null || true

# 256-color palette
RESET=$'\e[0m'
BOLD=$'\e[1m'
DIM=$'\e[38;5;240m'
C_REPO=$'\e[38;5;75m'
C_WORKTREE=$'\e[38;5;141m'
C_BRANCH=$'\e[38;5;114m'
C_BASE=$'\e[38;5;245m'
C_DIRTY=$'\e[38;5;215m'
C_CLEAN=$'\e[38;5;114m'
C_AHEAD=$'\e[38;5;111m'
C_STASH=$'\e[38;5;180m'
C_HOT=$'\e[38;5;202m'
C_TREES=$'\e[38;5;108m'
C_TREES_DIRTY=$'\e[38;5;215m'
C_TASK=$'\e[38;5;221m'
C_QUEUE=$'\e[38;5;245m'
C_VERSION=$'\e[38;5;208m'
C_MODEL=$'\e[38;5;177m'
C_CTX_OK=$'\e[38;5;156m'
C_CTX_WARN=$'\e[38;5;215m'
C_CTX_HOT=$'\e[38;5;203m'
C_COST=$'\e[38;5;156m'
SEP="${DIM}│${RESET}"

# --- Repo / worktree segment ---------------------------------------------
case "$CWD" in
  *.worktrees/*)
    WT_NAME=$(printf '%s' "$CWD" | sed -E 's|.*\.worktrees/([^/\\]+).*|\1|')
    REPO_SEG="${C_WORKTREE}🌳 ${WT_NAME}${RESET}"
    ;;
  *)
    REPO_SEG="${C_REPO}📁 main${RESET}"
    ;;
esac

# --- Resolve project dir (where .kris/ and .env live) --------------------
PROJ_DIR="$CWD"
while [ "$PROJ_DIR" != "/" ] && [ -n "$PROJ_DIR" ] && [ "$PROJ_DIR" != "." ]; do
  if [ -d "$PROJ_DIR/.kris" ] || [ -f "$PROJ_DIR/.env" ]; then
    break
  fi
  PARENT=$(dirname "$PROJ_DIR")
  [ "$PARENT" = "$PROJ_DIR" ] && break
  PROJ_DIR="$PARENT"
done

# --- Git segment ---------------------------------------------------------
GIT_SEG=""
STATUS_SEG=""
STASH_SEG=""
if git -C "$CWD" rev-parse --git-dir >/dev/null 2>&1; then
  BRANCH=$(git -C "$CWD" symbolic-ref --short HEAD 2>/dev/null)
  [ -z "$BRANCH" ] && BRANCH=$(git -C "$CWD" rev-parse --short HEAD 2>/dev/null)

  DIRTY=$(git -C "$CWD" status --porcelain 2>/dev/null | wc -l | tr -d ' ')

  AHEAD=0
  BEHIND=0
  UPSTREAM=$(git -C "$CWD" rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>/dev/null)
  if [ -n "$UPSTREAM" ]; then
    COUNTS=$(git -C "$CWD" rev-list --left-right --count "HEAD...$UPSTREAM" 2>/dev/null)
    AHEAD=$(printf '%s' "$COUNTS" | awk '{print $1+0}')
    BEHIND=$(printf '%s' "$COUNTS" | awk '{print $2+0}')
  fi

  STASH_COUNT=$(git -C "$CWD" stash list 2>/dev/null | wc -l | tr -d ' ')

  case "$BRANCH" in
    feature/*|bugfix/*|chore/*|refactor/*|fix/*) BASE_HINT="${C_BASE} →dev${RESET}" ;;
    hotfix/*) BASE_HINT="${C_BASE} →main${RESET}" ;;
    dev)      BASE_HINT="${C_BASE} →staging${RESET}" ;;
    staging)  BASE_HINT="${C_BASE} →beta${RESET}" ;;
    beta)     BASE_HINT="${C_BASE} →main${RESET}" ;;
    main)     BASE_HINT="${C_BASE} 🚀 prod${RESET}" ;;
    *)        BASE_HINT="" ;;
  esac

  GIT_SEG="${C_BRANCH}🌿 ${BRANCH}${RESET}${BASE_HINT}"

  if [ "${DIRTY:-0}" -gt 0 ]; then
    STATUS_SEG="${C_DIRTY}✏ ${DIRTY}${RESET}"
  else
    STATUS_SEG="${C_CLEAN}✓ clean${RESET}"
  fi
  [ "${AHEAD:-0}" -gt 0 ]  && STATUS_SEG="${STATUS_SEG} ${C_AHEAD}↑${AHEAD}${RESET}"
  [ "${BEHIND:-0}" -gt 0 ] && STATUS_SEG="${STATUS_SEG} ${C_AHEAD}↓${BEHIND}${RESET}"

  if [ "${STASH_COUNT:-0}" -gt 0 ]; then
    STASH_SEG="${C_STASH}📦 ${STASH_COUNT}${RESET}"
  fi
fi

# --- Activity: dev server (Laravel writes public/hot when vite is up) ----
HOT_SEG=""
if [ -f "$PROJ_DIR/public/hot" ]; then
  HOT_SEG="${C_HOT}🔥 vite${RESET}"
fi

# --- Cross-worktree activity (only meaningful in main session) -----------
# .worktrees/ may be inside PROJ_DIR or a sibling directory.
TREES_SEG=""
case "$CWD" in
  *.worktrees/*) ;;  # in a worktree → skip
  *)
    if [ -d "$PROJ_DIR/.worktrees" ]; then
      WT_PARENT="$PROJ_DIR/.worktrees"
    else
      WT_PARENT=$(dirname "$PROJ_DIR")/.worktrees
    fi
    if [ -d "$WT_PARENT" ]; then
      WT_TOTAL=0
      WT_ACTIVE=0
      for d in "$WT_PARENT"/*/; do
        [ -d "$d" ] || continue
        WT_TOTAL=$(( WT_TOTAL + 1 ))
        if ls "${d}.kris/tasks/running/"*.md >/dev/null 2>&1 || \
           ls "${d}backend/.kris/tasks/running/"*.md >/dev/null 2>&1; then
          WT_ACTIVE=$(( WT_ACTIVE + 1 ))
        fi
      done
      if [ "$WT_TOTAL" -gt 0 ]; then
        if [ "$WT_ACTIVE" -gt 0 ]; then
          TREES_SEG="${C_TREES}🌲 ${C_TREES_DIRTY}${WT_ACTIVE}${C_TREES}/${WT_TOTAL}${RESET}"
        else
          TREES_SEG="${C_TREES}🌲 ${WT_TOTAL}${RESET}"
        fi
      fi
    fi
    ;;
esac

# --- Active task --------------------------------------------------------
# 🎯 (yellow) = task matching this branch's issue # (your current focus)
# 📋 (gray)   = task(s) in running/ not tied to this branch (just FYI)
TASK_SEG=""
if [ -d "$PROJ_DIR/.kris/tasks/running" ]; then
  TASK_ISSUE=""
  case "$BRANCH" in
    dev|main|staging|beta) TASK_ISSUE="" ;;
    */[0-9]*) TASK_ISSUE=$(printf '%s' "$BRANCH" | grep -oE '/[0-9]+' | head -1 | tr -d '/') ;;
  esac

  MATCH_FILE=""
  if [ -n "$TASK_ISSUE" ]; then
    MATCH_FILE=$(ls -1 "$PROJ_DIR/.kris/tasks/running/"*"_${TASK_ISSUE}"-*.md 2>/dev/null | head -1)
    [ -z "$MATCH_FILE" ] && MATCH_FILE=$(ls -1 "$PROJ_DIR/.kris/tasks/running/"*"_${TASK_ISSUE}"[a-z]*-*.md 2>/dev/null | head -1)
  fi

  if [ -n "$MATCH_FILE" ]; then
    BASE=$(basename "$MATCH_FILE" .md)
    NAME=$(printf '%s' "$BASE" | sed -E 's/^[0-9]{4}-[0-9]{2}-[0-9]{2}_//')
    TASK_SEG="${C_TASK}🎯 ${NAME}${RESET}"
  else
    # No branch match → list whatever's in running/ as queue items.
    QUEUE_TOTAL=$(ls -1 "$PROJ_DIR/.kris/tasks/running/"*.md 2>/dev/null | wc -l | tr -d ' ')
    if [ "${QUEUE_TOTAL:-0}" -gt 0 ]; then
      FIRST=$(ls -1 "$PROJ_DIR/.kris/tasks/running/"*.md 2>/dev/null | head -1)
      BASE=$(basename "$FIRST" .md)
      NAME=$(printf '%s' "$BASE" | sed -E 's/^[0-9]{4}-[0-9]{2}-[0-9]{2}_//')
      if [ "$QUEUE_TOTAL" -gt 1 ]; then
        EXTRA=$(( QUEUE_TOTAL - 1 ))
        TASK_SEG="${C_QUEUE}📋 ${NAME} +${EXTRA}${RESET}"
      else
        TASK_SEG="${C_QUEUE}📋 ${NAME}${RESET}"
      fi
    fi
  fi
fi

# --- Version -------------------------------------------------------------
VERSION_SEG=""
if [ -f "$PROJ_DIR/.env" ]; then
  VER=$(grep -E '^APP_VERSION=' "$PROJ_DIR/.env" 2>/dev/null | head -1 \
        | sed -E 's/^APP_VERSION=//; s/^"//; s/"$//; s/^'\''//; s/'\''$//')
  if [ -n "$VER" ]; then
    VERSION_SEG="${C_VERSION}🏷 ${VER}${RESET}"
  fi
fi

# --- Model (with 1M badge) -----------------------------------------------
MODEL_SEG=""
if [ -n "$MODEL_NAME" ]; then
  SHORT=$(printf '%s' "$MODEL_NAME" | sed -E 's/^Claude //; s/ \(.*\)//')
  IS_1M=0
  case "${MODEL_ID}|${MODEL_NAME}" in
    *1m*|*1M*) IS_1M=1 ;;
  esac
  if [ "$IS_1M" = "1" ]; then
    MODEL_SEG="${C_MODEL}🤖 ${SHORT}${RESET} ${DIM}[1M]${RESET}"
  else
    MODEL_SEG="${C_MODEL}🤖 ${SHORT}${RESET}"
  fi
fi

# --- Context window % (always shown) -------------------------------------
# Threshold pinned at 100k — the practical "context rot" sweetspot.
# Past this, model performance degrades regardless of model max (200k / 1M).
# Percentage is uncapped: >100% means we're over budget.
LIMIT=100000

TOTAL=0
if [ -n "$TRANSCRIPT" ] && [ -f "$TRANSCRIPT" ]; then
  LAST_USAGE=$(tail -n 200 "$TRANSCRIPT" 2>/dev/null \
               | grep -oE '"usage":[[:space:]]*\{[^}]*\}' \
               | tail -1)
  if [ -n "$LAST_USAGE" ]; then
    INP=$(printf '%s' "$LAST_USAGE"   | grep -oE '"input_tokens":[[:space:]]*[0-9]+'                | grep -oE '[0-9]+' | head -1)
    CR=$(printf '%s' "$LAST_USAGE"    | grep -oE '"cache_read_input_tokens":[[:space:]]*[0-9]+'     | grep -oE '[0-9]+' | head -1)
    CC=$(printf '%s' "$LAST_USAGE"    | grep -oE '"cache_creation_input_tokens":[[:space:]]*[0-9]+' | grep -oE '[0-9]+' | head -1)
    TOTAL=$(( ${INP:-0} + ${CR:-0} + ${CC:-0} ))
  fi
fi

PCT=$(( TOTAL * 100 / LIMIT ))
USED=$(awk -v t="$TOTAL" 'BEGIN{
  if (t>=1000000) printf "%.1fM", t/1000000;
  else if (t>=1000)   printf "%.0fk", t/1000;
  else                printf "%d", t;
}')

BLINK=""
if   [ "$PCT" -ge 90 ]; then BLINK=$'\e[5m'; C_CTX="$C_CTX_HOT"
elif [ "$PCT" -ge 70 ]; then BLINK=$'\e[5m'; C_CTX="$C_CTX_WARN"
else                                          C_CTX="$C_CTX_OK"
fi
CTX_SEG="${BLINK}${C_CTX}📊 ${USED}/100k (${PCT}%)${RESET}"

# --- Cost (always shown, even at $0.00) ----------------------------------
COST_VAL="${COST:-0}"
[ "$COST_VAL" = "null" ] && COST_VAL=0
COST_FMT=$(awk -v c="$COST_VAL" 'BEGIN{printf "%.2f", c+0}')
COST_SEG="${C_COST}💰 \$${COST_FMT}${RESET}"

# --- 5-hour session window (rolling Anthropic rate-limit window) ---------
# Walks all ~/.claude/projects/*/*.jsonl transcripts modified in the last
# 6h, sums tokens within the active window, and derives reset time.
# Window starts at the first message after a >5h gap; ends 5h later.
# Cached to ~/.claude/cache/5h-window.cache for 30s to avoid rescanning.
WINDOW_SEG=""
LIMIT_5H="${CC_5H_LIMIT:-100000000}"  # default 100M tokens; set CC_5H_LIMIT to your plan
CACHE_DIR="$HOME/.claude/cache"
CACHE_FILE="$CACHE_DIR/5h-window.cache"
NOW_EPOCH=$(date +%s)

USE_CACHE=0
CACHED=""
if [ -f "$CACHE_FILE" ]; then
  CACHED=$(cat "$CACHE_FILE" 2>/dev/null)
  C_TIME=$(printf '%s' "$CACHED" | awk -F'|' '{print $1+0}')
  C_END=$(printf '%s' "$CACHED" | awk -F'|' '{print $2+0}')
  C_AGE=$(( NOW_EPOCH - C_TIME ))
  if [ "$C_AGE" -ge 0 ] && [ "$C_AGE" -lt 30 ] && [ "$NOW_EPOCH" -lt "$C_END" ]; then
    USE_CACHE=1
  fi
fi

if [ "$USE_CACHE" = "1" ]; then
  PCT_5H=$(printf '%s' "$CACHED" | awk -F'|' '{print $3}')
  RESET_HHMM=$(printf '%s' "$CACHED" | awk -F'|' '{print $4}')
  TOKENS_5H=$(printf '%s' "$CACHED" | awk -F'|' '{print $5+0}')
else
  RESULT=$(LIMIT_5H="$LIMIT_5H" python - <<'PY' 2>/dev/null
import os, re, glob, time
from datetime import datetime, timezone

LIMIT = int(os.environ.get('LIMIT_5H', '100000000') or '100000000')
WINDOW = 5 * 3600
now = time.time()
mtime_cutoff = now - 6 * 3600
ev_cutoff = now - 6 * 3600

projects = os.path.expanduser('~/.claude/projects')
ts_re = re.compile(r'"timestamp":"([^"]+)"')
usage_re = re.compile(r'"usage":\{([^}]*)\}')
field_input  = re.compile(r'"input_tokens":(\d+)')
field_cache_r = re.compile(r'"cache_read_input_tokens":(\d+)')
field_cache_c = re.compile(r'"cache_creation_input_tokens":(\d+)')
field_output = re.compile(r'"output_tokens":(\d+)')

events = []
for f in glob.glob(os.path.join(projects, '*', '*.jsonl')):
    try:
        if os.path.getmtime(f) < mtime_cutoff:
            continue
        with open(f, 'rb') as fh:
            for line in fh:
                s = line.decode('utf-8', 'ignore')
                m = ts_re.search(s)
                if not m:
                    continue
                ts = m.group(1).replace('Z', '+00:00')
                try:
                    dt = datetime.fromisoformat(ts)
                except Exception:
                    continue
                if dt.tzinfo is None:
                    dt = dt.replace(tzinfo=timezone.utc)
                ep = dt.timestamp()
                if ep < ev_cutoff or ep > now + 60:
                    continue
                u = usage_re.search(s)
                tok = 0
                if u:
                    body = u.group(1)
                    for rgx in (field_input, field_cache_r, field_cache_c, field_output):
                        mm = rgx.search(body)
                        if mm:
                            tok += int(mm.group(1))
                events.append((ep, tok))
    except Exception:
        continue

events.sort()

window_start = None
window_end_ep = None
window_tokens = 0
chain_start = None
chain_tokens = 0
for ep, tok in events:
    if chain_start is None or (ep - chain_start) > WINDOW:
        chain_start = ep
        chain_tokens = tok
    else:
        chain_tokens += tok
    end_candidate = chain_start + WINDOW
    if end_candidate > now:
        window_start = chain_start
        window_end_ep = end_candidate
        window_tokens = chain_tokens

if window_start is None:
    print('0|0.0||0')
else:
    pct = window_tokens * 100.0 / LIMIT if LIMIT > 0 else 0.0
    reset = datetime.fromtimestamp(window_end_ep).strftime('%H:%M')
    print(f'{int(window_end_ep)}|{pct:.1f}|{reset}|{window_tokens}')
PY
)
  C_END=$(printf '%s' "$RESULT" | awk -F'|' '{print $1+0}')
  PCT_5H=$(printf '%s' "$RESULT" | awk -F'|' '{print $2}')
  RESET_HHMM=$(printf '%s' "$RESULT" | awk -F'|' '{print $3}')
  TOKENS_5H=$(printf '%s' "$RESULT" | awk -F'|' '{print $4+0}')
  mkdir -p "$CACHE_DIR" 2>/dev/null
  printf '%s|%s|%s|%s|%s\n' "$NOW_EPOCH" "$C_END" "$PCT_5H" "$RESET_HHMM" "$TOKENS_5H" > "$CACHE_FILE" 2>/dev/null
fi

if [ -n "$RESET_HHMM" ]; then
  PCT_INT=$(awk -v p="$PCT_5H" 'BEGIN{printf "%d", p+0}')
  W_BLINK=""
  if   [ "$PCT_INT" -ge 90 ]; then W_BLINK=$'\e[5m'; C_W="$C_CTX_HOT"
  elif [ "$PCT_INT" -ge 70 ]; then W_BLINK=$'\e[5m'; C_W="$C_CTX_WARN"
  else                                                C_W="$C_CTX_OK"
  fi
  WINDOW_SEG="${W_BLINK}${C_W}⏱ ${PCT_5H}% · resets ${RESET_HHMM}${RESET}"
fi

# --- Compose four lines --------------------------------------------------
LINE1=""
LINE2=""
LINE3=""
LINE4=""
add_to() {
  local var="$1"; local seg="$2"
  [ -z "$seg" ] && return
  local cur
  eval "cur=\${$var}"
  if [ -z "$cur" ]; then
    eval "$var=\$seg"
  else
    eval "$var=\"\$cur \${SEP} \$seg\""
  fi
}

# Line 1: where am I — location + git + activity
add_to LINE1 "$REPO_SEG"
add_to LINE1 "$GIT_SEG"
add_to LINE1 "$STATUS_SEG"
add_to LINE1 "$STASH_SEG"
add_to LINE1 "$TREES_SEG"
add_to LINE1 "$HOT_SEG"

# Line 2: what am I working on — task + version
add_to LINE2 "$TASK_SEG"
add_to LINE2 "$VERSION_SEG"

# Line 3: this conversation — model + ctx + cost
add_to LINE3 "$MODEL_SEG"
add_to LINE3 "$CTX_SEG"
add_to LINE3 "$COST_SEG"

# Line 4: 5-hour rate-limit window
add_to LINE4 "$WINDOW_SEG"

printf '%s\n%s\n%s\n%s' "$LINE1" "$LINE2" "$LINE3" "$LINE4"
