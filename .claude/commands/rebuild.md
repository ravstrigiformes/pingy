# /rebuild - Pull & Rebuild Docker Environment

Pull latest changes and rebuild Docker containers with a clean cache.

---

## Quick Start

```bash
/rebuild                  # Pull, rebuild all images (no cache), clear Laravel caches
/rebuild --quick          # Pull, rebuild without --no-cache, clear Laravel caches
/rebuild --no-pull        # Skip git pull, just rebuild and clear caches
```

---

## How It Works

### Phase 1: Pull Latest Changes

```bash
git pull
```

- Skip this phase if `--no-pull` flag is provided
- If pull fails (conflicts, dirty working tree), stop and report — do not force

### Phase 2: Rebuild Docker Containers

```bash
# Stop all running containers
docker compose down

# Rebuild all images
docker compose build --no-cache    # default: full clean rebuild
docker compose build               # with --quick: use layer cache

# Start containers
docker compose up -d
```

- Wait for all health checks to pass before proceeding
- If any container fails to start, check logs with `docker compose logs <service>` and report

### Phase 3: Clear Laravel Caches

```bash
docker compose exec app php artisan optimize:clear
```

This clears: config, cache, compiled, events, routes, views.

### Phase 4: Verify

```bash
# Confirm all containers are running and healthy
docker compose ps
```

Report final status: which containers are up, any warnings.

---

## Flags

### `--quick` or `-q`

Skip `--no-cache` on the build step — use Docker layer cache for faster rebuilds when only code changed (no Dockerfile/dependency changes):

```bash
/rebuild --quick
```

### `--no-pull` or `-n`

Skip the git pull step — useful when you've already pulled or want to rebuild from current state:

```bash
/rebuild --no-pull
```

### `--migrate` or `-m`

Run migrations explicitly after rebuild (normally handled by entrypoint, but useful if entrypoint was skipped or you want to confirm):

```bash
/rebuild --migrate
```

```bash
docker compose exec app php artisan migrate --force
```

---

## Output Format

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
REBUILD
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

PULL
   Branch: staging
   Result: Fast-forward, 46 files changed

BUILD
   Mode: --no-cache (clean rebuild)
   Images: app, nginx, worker
   Status: All built successfully

CONTAINERS
   mysql .................. healthy
   redis .................. healthy
   app .................... healthy
   worker ................. healthy
   nginx .................. healthy

CACHE
   optimize:clear ......... done (config, cache, compiled, events, routes, views)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Rebuild complete.
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## Error Handling

| Error | Resolution |
|-------|------------|
| Git pull fails | Report conflict/error, do not force |
| Build fails | Show build logs for the failing image |
| Container won't start | Show `docker compose logs <service>` output |
| Health check fails | Wait up to 60s, then report with logs |
| Cache clear fails | Container may not be ready — retry once after 5s |

---

## Integration with Other Skills

| Skill | Relationship |
|-------|-------------|
| `/stage` | After promoting changes, use `/rebuild` on the target environment |
| `/hotfix` | After hotfix lands, `/rebuild` to deploy |
| `/dev` | After merging PR to dev, `/rebuild` if on dev environment |
