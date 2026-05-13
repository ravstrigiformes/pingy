# 010 — Local dev compose (Postgres + Redis + MinIO + test KDC + nginx-SPNEGO)

**Status:** pending · **Owner:** unassigned · **Depends on:** none

## Why

Backend dev (W2 onwards in plan §10) needs a one-command local stack that mirrors the on-prem prod profile. Without this, devs hand-stand Postgres / Redis / object storage and Kerberos becomes "I'll figure it out later" — which is exactly when v1 risk #1 (Kerberos misconfig) bites at deploy time.

## Scope

Create `infra/docker/compose.yml` that brings up:

| Service | Image | Purpose |
|---------|-------|---------|
| `postgres` | `postgres:16` | Primary datastore |
| `redis` | `redis:7-alpine` | Horizon queue backing |
| `minio` | `minio/minio` | S3-compatible diagnostic blob storage |
| `kdc` | `gcavalcante8808/krb5-server` (or build from `ubuntu:22.04` + `krb5-kdc`) | Local KDC for Kerberos integration tests |
| `nginx-spnego` | Custom build of `nginx` + Bit4Id `spnego-http-auth-nginx-module` | SPNEGO reverse proxy in front of php-fpm |

Plus a bootstrap script `infra/docker/bootstrap.sh` that:

1. Creates the test KDC realm `PINGY.LOCAL`
2. Creates an HTTP service principal `HTTP/pingy.pingy.local`
3. Exports the keytab to `infra/keytabs/pingy.keytab` (gitignored)
4. Configures nginx to use that keytab
5. Outputs a `kinit -kt` command for testing

## Acceptance criteria

- `docker compose -f infra/docker/compose.yml up -d` brings everything up cleanly
- `docker compose logs nginx-spnego` shows it bound to port 8080 with SPNEGO ready
- From host: `klist` after `kinit -kt infra/keytabs/pingy.keytab HTTP/pingy.pingy.local` shows a valid TGT
- `curl --negotiate -u : http://pingy.pingy.local:8080/api/v1/whoami` returns 200 with the principal in JSON (after the `/whoami` endpoint exists from task 040)
- `infra/keytabs/pingy.keytab` is gitignored (already covered by `.gitignore`)
- README in `infra/docker/` explains how to add `pingy.pingy.local` to the host `hosts` file

## Files

- `infra/docker/compose.yml` (new)
- `infra/docker/nginx/Dockerfile` (new — for SPNEGO module build)
- `infra/docker/nginx/nginx.conf` (new)
- `infra/docker/bootstrap.sh` (new)
- `infra/docker/README.md` (new)
- `infra/keytabs/.gitkeep` (already exists)

## Design notes

- Use a custom `pingy.pingy.local` rather than `localhost` because Kerberos requires a stable hostname matching the SPN
- KDC and nginx must be on the same docker network so SPNEGO traffic resolves internally
- Don't expose KDC's port 88 publicly — only inside the compose network and host loopback for testing
- For the SPNEGO nginx module, the Bit4Id fork (`spnego-http-auth-nginx-module`) is the maintained one referenced in plan §4
- Postgres should run with `shared_preload_libraries=pg_stat_statements,pg_partman_bgw` to be ready for the partitioning work in task 050+

## Out of scope

- Production infra (terraform/ansible — that's separate, when stack is happy)
- pgbouncer (add when ingest endpoint exists)
- TLS termination (dev uses HTTP, prod adds TLS at the LB)
