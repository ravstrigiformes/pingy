# 030 — Install + configure Laravel ecosystem packages

**Status:** pending · **Owner:** unassigned · **Depends on:** Laravel scaffold (already done in W1)

## Why

ONBOARDING §7 lists the packages the backend needs but defers their install to the next session so they pin against the internal mirror cleanly. This task does the install + configures `.env` + publishes Horizon assets + initializes Pest.

## Scope

Run inside `server/`:

```bash
composer require laravel/horizon
composer require livewire/livewire
composer require --dev pestphp/pest pestphp/pest-plugin-laravel
composer require --dev larastan/larastan
composer require --dev qossmic/deptrac-shim

php artisan horizon:install
php artisan vendor:publish --provider="Laravel\Horizon\HorizonServiceProvider"
./vendor/bin/pest --init
```

Then configure `server/.env` (copy from `.env.example` first):

```env
DB_CONNECTION=pgsql
DB_HOST=127.0.0.1
DB_PORT=5432
DB_DATABASE=pingy
DB_USERNAME=pingy
DB_PASSWORD=...
QUEUE_CONNECTION=redis
REDIS_HOST=127.0.0.1
REDIS_PORT=6379
```

Add a `phpstan.neon` at `server/` configured for Larastan with level 6+. Add a `deptrac.yaml` at `server/` with module boundary rules (will be expanded in task 040).

## Acceptance criteria

- `composer install` succeeds in a clean clone after these packages are added
- `./vendor/bin/pest` runs (even with zero tests) without errors
- `./vendor/bin/phpstan analyse` runs (will likely emit findings — that's fine, just must not crash)
- `./vendor/bin/deptrac analyse` runs (no rules yet = no violations)
- `php artisan horizon:status` works against the dev compose Redis
- `php artisan livewire:publish --config` shows Livewire is installed
- `.env.example` is updated with the new vars (DB_, REDIS_) so other devs know what to set

## Files

- `server/composer.json` (modified)
- `server/composer.lock` (modified)
- `server/config/horizon.php` (auto-generated; review for sensible defaults)
- `server/tests/Pest.php` (Pest init creates this)
- `server/phpstan.neon` (new)
- `server/deptrac.yaml` (new — minimal config; rules grow in task 040)
- `server/.env.example` (modified)

## Design notes

- For **air-gap**: Composer must be configured to use the org's internal Packagist mirror (Satis/Toran Proxy). Until that's set up, fine to run against `packagist.org` for dev — flag it as a follow-up
- Horizon's published config should set `'environments' => ['production' => [...]]` with reasonable supervisor configs for `ingest` and `tickets` queues (per plan §4)
- Pest plugins to consider in the future: `pest-plugin-arch` (for arch tests enforcing modular boundaries)
- Keep `larastan` strict: starting at level 6 is honest; level 9 is the eventual goal

## Out of scope

- Actually writing tests (separate per-module tasks)
- Configuring Horizon supervisors for prod (W7 task)
- Adding modular monolith Deptrac rules (task 040)
