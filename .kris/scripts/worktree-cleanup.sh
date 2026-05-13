#!/bin/bash
# Cleans up stale worktree entries and optionally removes abandoned worktree directories

set -e

echo "Pruning stale worktree entries..."
git worktree prune

echo ""
echo "Current worktrees after cleanup:"
git worktree list
echo ""

# Check for orphaned directories in .worktrees
WORKTREES_DIR="../.worktrees"
if [ -d "$WORKTREES_DIR" ]; then
    echo "Checking for orphaned directories in $WORKTREES_DIR..."

    for dir in "$WORKTREES_DIR"/*; do
        if [ -d "$dir" ]; then
            dirname=$(basename "$dir")
            # Check if this directory is tracked by git worktree
            if ! git worktree list | grep -q "$dir"; then
                echo "  Found orphaned directory: $dir"
                read -p "  Delete it? [y/N] " -n 1 -r
                echo
                if [[ $REPLY =~ ^[Yy]$ ]]; then
                    rm -rf "$dir"
                    echo "  Deleted: $dir"
                fi
            fi
        fi
    done
fi

echo ""
echo "Cleanup complete."
