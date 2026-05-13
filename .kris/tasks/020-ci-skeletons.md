# 020 — CI workflow skeletons (provider-agnostic)

**Status:** pending · **Owner:** unassigned · **Depends on:** 010 (dev compose) is recommended for local validation but not strictly required

## Why

Plan §6 + ONBOARDING §1 lock this project to **on-prem CI** (Gitea + Jenkins / Drone / Woodpecker / GitLab CE). The exact tool isn't decided yet, so the pipelines should be written generically enough that we can re-target without rewriting. Having pipeline files in `.ci/` from day 1 also forces us to keep the build green as code lands.

## Scope

Create two pipeline files in `.ci/`:

1. **`client.yml`** — runs on a Windows runner (build VM with .NET 8 SDK, WiX 4 Toolset, signtool)
   - Restore + build `client/Pingy.sln`
   - Run xUnit tests
   - `dotnet format --verify-no-changes`
   - WiX MSI build (placeholder — actual signing comes in W4 per plan §10)
   - Upload artifact

2. **`server.yml`** — runs on a Linux runner with PHP 8.3 + Composer 2
   - `composer install --no-interaction --no-progress`
   - `./vendor/bin/pest`
   - `./vendor/bin/pint --test`
   - `./vendor/bin/phpstan analyse`
   - `./vendor/bin/deptrac analyse` (after task 040 sets up Deptrac config)
   - Upload coverage report

Plus a `README.md` in `.ci/` documenting:
- Variables the pipeline expects (e.g., `INTERNAL_NUGET_FEED`, `INTERNAL_COMPOSER_REPO`)
- Caching strategy (NuGet packages, Composer vendor)
- How to translate the YAML to the eventual chosen runner (one-pager mapping table)

## Acceptance criteria

- Both YAML files are valid YAML and follow common syntax (jobs/steps style — close enough to GitLab CI / Drone / Jenkins declarative for easy port)
- `client.yml` would build and test the client given a Windows runner with the documented prerequisites
- `server.yml` would build and test the server given a Linux runner with PHP 8.3 + Composer 2
- README explains the air-gap requirements (point at internal mirrors only — no public NuGet/Packagist)
- No secrets in the YAML files; everything via documented variable references

## Files

- `.ci/client.yml` (new)
- `.ci/server.yml` (new)
- `.ci/README.md` (new)

## Design notes

- Don't pick a CI tool in this task — write portable enough that any of Gitea Actions / Drone / Woodpecker / GitLab CI can adopt
- For NuGet/Composer caching, use the runner-native cache directives if portable; otherwise document it as a follow-up
- Per plan §7, signing happens on a separate Windows runner with HSM/cert attached — keep signing OUT of this CI definition (separate job that promotes built artifacts later)
- Air-gap principle: package restore commands should reference internal mirrors, not public registries — leave the URLs as variables (`$INTERNAL_NUGET_FEED`) so they can be filled per env

## Out of scope

- Actual signing pipeline (separate task once HSM is configured in W4)
- Deployment to SCCM/GPO (W7+ work)
- Tagging/release automation
