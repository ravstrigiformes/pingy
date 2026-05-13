# Thread-Based Engineering

Reference for thread-based engineering patterns used with Claude Code and agentic workflows.

Source: [Thread-Based Engineering Guide](https://claudefa.st/blog/guide/mechanics/thread-based-engineering) | [Thinking in Threads](https://agenticengineer.com/thinking-in-threads)

---

## Core Concept

A **thread** is a unit of engineering work over time, driven by you and your agent. You show up at two nodes:
1. **Beginning** — prompt or plan
2. **End** — review or validate

Between these, agents execute tool calls autonomously.

---

## Thread Types

### Base Thread
**Pattern:** One prompt -> agent work -> one review
**When to use:** Simple tasks, quick fixes, single-file changes

### P-Thread (Parallel)
**Pattern:** Multiple independent threads running simultaneously
**When to use:** Independent tasks, code reviews, feature branches, research

**Implementation:**
- Open multiple terminal windows or Claude Code instances
- Use web interface for additional background agents
- Assign each instance one independent task
- Aggregate results when all complete

**Anti-pattern:** Parallelizing tasks that have context conflicts or dependencies

### C-Thread (Chained)
**Pattern:** Multi-phase work with human checkpoints between phases
**When to use:** Production deployments, large refactors, sensitive migrations, high-risk changes

**Example phases:**
1. Database migration -> review
2. API updates -> review
3. Frontend changes -> review

**Anti-pattern:** C-threading when risk doesn't justify the overhead

### F-Thread (Fusion)
**Pattern:** Same prompt to multiple agents, pick the best result ("best of N")
**When to use:** Rapid prototyping, architecture decisions, confidence-critical reviews

**Implementation:**
1. Formulate precise prompt
2. Dispatch to 4+ agent instances simultaneously
3. Review all solutions
4. Select best solution or cherry-pick elements from multiple

**Philosophy:** More agents trying = higher chance of success. Four perspectives beat one.

**Anti-pattern:** Merging incompatible solutions without clear selection criteria

### B-Thread (Big/Meta)
**Pattern:** Orchestrator thread that spawns and manages sub-agent threads
**When to use:** Complex multi-file changes, team-of-agents workflows

**Implementation:**
1. Orchestrator agent receives high-level plan
2. Spawns specialized sub-agents (frontend, backend, QA)
3. Each runs in isolated context
4. Results aggregate back to orchestrator

**Anti-pattern:** Nesting threads when a simpler approach would suffice

### L-Thread (Long)
**Pattern:** Extended autonomy, 100+ tool calls, minimal human intervention
**When to use:** Overnight builds, large codebases, backlog clearing, 5+ hour automation

**Requirements:**
- Comprehensive prompt with detailed success criteria
- Stop hook validation (prevents premature exit)
- Checkpoint state preservation
- Robust verification mechanisms

**Anti-pattern:** Running without verification — agent may stop prematurely

### Z-Thread (Zero-touch)
**Pattern:** Full autonomy, no human review
**When to use:** Future state — only for systems with extensive verification guardrails and production observability

---

## The Core Four Fundamentals

All optimizations target these elements:

| Element | Description |
|---------|-------------|
| **Context** | What agents know (improve through engineering) |
| **Model** | Which Claude version (select appropriately) |
| **Prompt** | What you're asking (precision matters) |
| **Tools** | What agents can execute (capability expansion) |

---

## Measuring Improvement

| Dimension | Target | How to Improve |
|-----------|--------|----------------|
| **More threads** | 3-15 concurrent | Add terminal windows, web instances |
| **Longer threads** | More tool calls per thread | Better planning, stop hooks |
| **Thicker threads** | More work per prompt | Nest sub-threads in B-threads |
| **Fewer checkpoints** | Less manual review | Build verification, trust system |

---

## Stop Hook Pattern (Critical for L-Threads)

Prevents premature agent exits:

1. Agent attempts completion
2. Stop hook intercepts
3. Validation code runs
4. If incomplete: block stop, force iteration
5. If complete: allow completion

---

## Mapping to Our Workflow

| Our Pattern | Thread Type | Notes |
|-------------|-------------|-------|
| `/worktree` + `/do` | **P-Thread** | Parallel worktrees = parallel threads |
| `/worktree` dispatching sub-agents | **B-Thread** | Orchestrator spawns isolated workers |
| Sequential `/stage` promotions | **C-Thread** | dev -> staging -> beta -> main with checkpoints |
| Single `/fix` or `/commit` | **Base Thread** | One task, one review |
| Multiple worktrees on same task | **F-Thread** | Best-of-N prototyping |
| Long `/do` sessions | **L-Thread** | Extended autonomy with task criteria as validation |

---

## Implementation Status

### F-Thread Planning (IMPLEMENTED)
Integrated into `/worktree` as **Phase 4.5: F-Thread Planning**:
- Auto-enabled for L/XL tasks, or via `--fusion` flag
- 4 parallel planning agents analyze the same task
- Orchestrator fuses best ideas into "ultimate plan"
- Fused plan becomes the implementation agent's brief
- Skippable with `--no-fusion`

### Future Opportunities

**L-Thread Enhancement:**
- Stop hook validation against task acceptance criteria
- Automatic retry on premature completion
- Progress checkpointing for long sessions

**B-Thread Execution (considered, deferred):**
F-Thread for execution (multiple agents implementing the same task) was considered but rejected because code isn't cherry-pickable like plans — merge conflicts and architectural incompatibilities make fusion impractical. Instead, the fused plan feeds a single implementation agent, which produces better results than trying to merge incompatible code.
