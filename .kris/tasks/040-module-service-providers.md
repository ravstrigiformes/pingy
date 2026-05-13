# 040 — Module ServiceProviders + Deptrac module rules

**Status:** pending · **Owner:** unassigned · **Depends on:** 030 (Deptrac shim installed)

## Why

The 6 module folders exist (`server/app/Modules/{Auth,Ingest,Targets,Tickets,Machines,Dashboard}/`) but they're empty scaffolds. They need:
1. A `ServiceProvider` per module that registers the module's routes/migrations/views
2. Registration in `bootstrap/providers.php` so Laravel actually loads them
3. Deptrac rules in `server/deptrac.yaml` that enforce the modular monolith boundary (per plan §4: cross-module calls only via `app/Shared/Contracts`)

## Scope

For each module create:

```
server/app/Modules/{Module}/Providers/{Module}ServiceProvider.php
```

Each provider's `register()`/`boot()` should:
- Load module-specific routes (`Routes/api.php` if present)
- Load module-specific migrations from `Database/Migrations/`
- Load module-specific views (only `Dashboard` will need this)
- Bind any module-internal interfaces

Register in `server/bootstrap/providers.php`:

```php
return [
    App\Providers\AppServiceProvider::class,
    App\Modules\Auth\Providers\AuthServiceProvider::class,
    App\Modules\Machines\Providers\MachinesServiceProvider::class,
    App\Modules\Targets\Providers\TargetsServiceProvider::class,
    App\Modules\Ingest\Providers\IngestServiceProvider::class,
    App\Modules\Tickets\Providers\TicketsServiceProvider::class,
    App\Modules\Dashboard\Providers\DashboardServiceProvider::class,
];
```

Add a stub `Routes/api.php` to each module returning an empty group.

Configure `server/deptrac.yaml` with these layers + rules:

```yaml
deptrac:
  paths:
    - ./app
  layers:
    - name: AuthModule
      collectors: [{type: directory, value: app/Modules/Auth/.*}]
    - name: MachinesModule
      collectors: [{type: directory, value: app/Modules/Machines/.*}]
    # ...same for Targets, Ingest, Tickets, Dashboard
    - name: SharedContracts
      collectors: [{type: directory, value: app/Shared/Contracts/.*}]
    - name: SharedDTOs
      collectors: [{type: directory, value: app/Shared/DTOs/.*}]
  ruleset:
    AuthModule: [SharedContracts, SharedDTOs]
    MachinesModule: [SharedContracts, SharedDTOs]
    TargetsModule: [SharedContracts, SharedDTOs]
    IngestModule: [SharedContracts, SharedDTOs, MachinesModule, TargetsModule]
      # Ingest writes telemetry referencing machines+targets — narrow exception
    TicketsModule: [SharedContracts, SharedDTOs, MachinesModule]
    DashboardModule: [SharedContracts, SharedDTOs]  # read-only via contracts
    SharedContracts: ~
    SharedDTOs: ~
```

## Acceptance criteria

- `php artisan route:list` shows API routes from each module (even if empty groups)
- `php artisan config:cache && php artisan route:cache` succeed without errors
- `./vendor/bin/deptrac analyse` reports zero violations on the current empty modules
- `php artisan tinker` can `app(App\Modules\Auth\Providers\AuthServiceProvider::class)` (proves DI registration)

## Files

- `server/app/Modules/Auth/Providers/AuthServiceProvider.php` (new)
- `server/app/Modules/Auth/Routes/api.php` (new, empty group)
- ...same pattern for Machines, Targets, Ingest, Tickets, Dashboard
- `server/bootstrap/providers.php` (modified)
- `server/deptrac.yaml` (modified)

## Design notes

- The provider pattern keeps modules self-contained — their routes/migrations/views are colocated, not spread into top-level Laravel folders
- Deptrac's ruleset is the **enforcement layer** for ONBOARDING §1.3 ("cross-module calls via Contracts only"). Without it, modules silently couple
- Ingest's exception (allowed to depend on Machines + Targets) is pragmatic: the hot-path needs to look up machine_id and target_id without an extra Contract layer indirection. Keep this exception narrow
- Dashboard reads via Contracts only — it should never directly query module models. Enforce strictly

## Out of scope

- Actual module logic (each module gets its own task: 050+ for Auth, 060+ for Ingest, etc.)
- Migration files (a separate per-module task per plan §5 schema)
