#!/bin/bash
# Usage: .kris/scripts/worktree-remove.sh task-name
# Removes: ../.worktrees/feature-task-name (keeps branch for PR)

set -e

TASK_NAME=$1
if [ -z "$TASK_NAME" ]; then
    echo "Usage: .kris/scripts/worktree-remove.sh <task-name>"
    echo "Example: .kris/scripts/worktree-remove.sh add-logout-button"
    exit 1
fi

WORKTREE_PATH="../.worktrees/feature-$TASK_NAME"
BRANCH_NAME="feature/$TASK_NAME"

if [ ! -d "$WORKTREE_PATH" ]; then
    echo "Error: Worktree not found at $WORKTREE_PATH"
    echo ""
    echo "Available worktrees:"
    git worktree list
    exit 1
fi

echo "Removing worktree at $WORKTREE_PATH..."
git worktree remove "$WORKTREE_PATH"

echo ""
echo "========================================="
echo "Worktree removed successfully!"
echo "========================================="
echo ""
echo "Branch '$BRANCH_NAME' still exists."
echo ""
echo "To create a PR, run:"
echo ""
echo "  gh pr create --base dev --head $BRANCH_NAME --title \"feat: description\""
echo ""
