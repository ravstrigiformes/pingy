---
name: grill-me
description: Interview the user relentlessly about a plan or design until reaching shared understanding, resolving each branch of the decision tree. Use when user wants to stress-test a plan, get grilled on their design, or mentions "grill me".
---

Interview me relentlessly about every aspect of this plan until we reach a shared understanding. Walk down each branch of the design tree, resolving dependencies between decisions one-by-one. For each question, provide your recommended answer.

Ask the questions one at a time.

If a question can be answered by exploring the codebase, explore the codebase instead.

Prefer `AskUserQuestion` over plain prose questions — it surfaces options clearly and lets the user pick. When using it:
- Frame each option as a concrete, actionable choice (not a vague stance)
- Mark your recommended option with "(Recommended)" in the label
- Include enough description that the user can pick without having to re-read the question

When all branches resolve, summarize the locked-in decisions in a short bullet list before proceeding.
