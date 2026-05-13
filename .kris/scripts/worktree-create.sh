#!/bin/bash
# Usage: .kris/scripts/worktree-create.sh task-name
# Creates: ../.worktrees/feature-task-name with branch feature/task-name

set -e

TASK_NAME=$1
if [ -z "$TASK_NAME" ]; then
    echo "Usage: .kris/scripts/worktree-create.sh <task-name>"
    echo "Example: .kris/scripts/worktree-create.sh add-logout-button"
    exit 1
fi

BRANCH_NAME="feature/$TASK_NAME"
WORKTREE_PATH="../.worktrees/feature-$TASK_NAME"

# Check if worktree already exists
if [ -d "$WORKTREE_PATH" ]; then
    echo "Error: Worktree already exists at $WORKTREE_PATH"
    exit 1
fi

# Fetch latest dev
echo "Fetching latest dev..."
git fetch origin dev

# Create worktree with new branch from dev
echo "Creating worktree..."
git worktree add "$WORKTREE_PATH" -b "$BRANCH_NAME" origin/dev

echo ""
echo "========================================="
echo "Worktree created successfully!"
echo "========================================="
echo ""
echo "  Path:   $WORKTREE_PATH"
echo "  Branch: $BRANCH_NAME"
echo ""
echo "To start working, run:"
echo ""
echo "  cd $WORKTREE_PATH && claude"
echo ""
