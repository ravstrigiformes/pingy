---
name: schema-sync
description: Synchronize database schema with migration definitions - detect and fix schema drift in production
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, AskUserQuestion
user-invocable: true
---

# Schema Sync

A skill for detecting and fixing database schema drift by comparing the current database structure with Laravel migration definitions. Generates safe, reviewable migration files for production environments.

## Philosophy

- **Safety first** - Generate migration files for review, never raw SQL execution
- **Interactive by default** - Confirm critical/destructive changes with user
- **Audit trail** - All changes tracked via standard Laravel migrations
- **Multi-database aware** - Handles multiple database connections gracefully

---

## Invocation

### `/schema-sync` (default - interactive)

Full interactive mode:
1. Discovers all database connections
2. Introspects current schema
3. Parses migrations to determine expected schema
4. Computes diff and shows report
5. Asks for confirmation on critical changes
6. Generates a corrective migration file

### `/schema-sync --dry-run`

Preview mode - shows what would change without generating any migration.

### `/schema-sync --force`

Skip all confirmations (for CI/CD or scripted use). Still generates migration file.

### `/schema-sync --connection={name}`

Target a specific database connection only.

### `/schema-sync --table={name}`

Target a specific table only.

---

## Multi-Database Architecture

This project uses multiple database connections defined in `config/database.php`. The skill must:

1. **Discover connections** - Read `config/database.php` and `config/database.module_connections`
2. **Map tables to connections** - Parse migrations to understand which tables belong to which connection
3. **Handle cross-connection references** - Be aware that some tables reference others across connections

### Connection Discovery

```php
// Via tinker - get all configured connections
$connections = array_keys(config('database.connections'));
$moduleConnections = config('database.module_connections', []);
```

### Migration Connection Detection

Migrations specify their connection via:
```php
public function getConnection(): ?string
{
    return config('database.module_connections.uacs');
}
```

Or inline:
```php
Schema::connection('uacs')->create('table', ...);
```

---

## Step-by-Step Process

### Step 1 - Discovery

**1.1 Discover Database Connections**

Read `config/database.php` to find all connections:
- Default connection
- Module-specific connections (UACS, etc.)

Use tinker to verify active connections:
```php
// List all connections
collect(config('database.connections'))->keys()->all();

// Check module connections
config('database.module_connections');
```

**1.2 Discover Migrations**

Glob all migration files:
```
database/migrations/*.php
```

Parse each migration to extract:
- Connection (from `getConnection()` or `Schema::connection()`)
- Table name
- Column definitions
- Indexes
- Foreign keys

### Step 2 - Schema Introspection

**2.1 Current Schema (from Database)**

For each connection, use Doctrine DBAL via tinker:

```php
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Facades\DB;

// Get all tables for a connection
$tables = Schema::connection($conn)->getTableListing();

// For each table, get columns
foreach ($tables as $table) {
    $columns = Schema::connection($conn)->getColumnListing($table);

    // Get detailed column info via Doctrine
    $doctrine = DB::connection($conn)->getDoctrineSchemaManager();
    $tableDetails = $doctrine->introspectTable($table);

    foreach ($tableDetails->getColumns() as $column) {
        // $column->getName()
        // $column->getType()->getName()
        // $column->getNotnull()
        // $column->getDefault()
        // $column->getLength()
        // etc.
    }

    // Get indexes
    foreach ($tableDetails->getIndexes() as $index) {
        // $index->getName()
        // $index->getColumns()
        // $index->isUnique()
        // $index->isPrimary()
    }

    // Get foreign keys
    foreach ($tableDetails->getForeignKeys() as $fk) {
        // $fk->getName()
        // $fk->getLocalColumns()
        // $fk->getForeignTableName()
        // $fk->getForeignColumns()
    }
}
```

**2.2 Expected Schema (from Migrations)**

Parse migration files to extract expected schema. This is complex because migrations can:
- Use Blueprint methods (`$table->string('name')`)
- Have conditionals
- Call methods on other classes
- Use traits

**Parsing Strategy:**

1. Read migration file content
2. Use regex to extract `Schema::create` and `Schema::table` blocks
3. Parse Blueprint method calls:
   - `$table->string('column', length)` -> string column
   - `$table->integer('column')` -> integer column
   - `$table->foreignId('column')` -> foreign key
   - `$table->index('column')` -> index
   - etc.

4. Handle `createUnlessProtected` pattern used in this project

**Column Type Mapping:**

| Blueprint Method | Doctrine Type | MySQL Type |
|-----------------|---------------|------------|
| `string($col, $len)` | string | varchar($len) |
| `text($col)` | text | text |
| `integer($col)` | integer | int |
| `bigInteger($col)` | bigint | bigint |
| `boolean($col)` | boolean | tinyint(1) |
| `decimal($col, $p, $s)` | decimal | decimal($p,$s) |
| `timestamp($col)` | datetime | timestamp |
| `json($col)` | json | json |
| `foreignId($col)` | bigint | bigint unsigned |

### Step 3 - Diff Computation

Compare expected vs actual schema for each table:

**3.1 Missing Tables**
Tables in migrations but not in database.

**3.2 Extra Tables**
Tables in database but not in migrations.
- These are usually okay (created by packages, etc.)
- Warn but don't suggest deletion unless user confirms

**3.3 Missing Columns**
Columns defined in migrations but not in database.
- Generate `$table->addColumn(...)` statements

**3.4 Extra Columns**
Columns in database but not in migrations.
- **Critical decision point** - could be:
  - Intentionally added manually (keep)
  - Leftover from old migration (delete)
  - Renamed column (the old name)
- Ask user what to do

**3.5 Column Type Mismatches**
Column exists but with wrong type/attributes.
- Generate `$table->type('column')->change()` statements
- Requires doctrine/dbal

**3.6 Renamed Columns (Heuristic Detection)**

When we see:
- Column A exists in DB but not in migrations
- Column B exists in migrations but not in DB
- A and B have same type/attributes

This suggests a rename. Score candidates by:
- Same data type: +3 points
- Same nullable: +2 points
- Same default: +1 point
- Similar name (Levenshtein distance < 3): +2 points

If score >= 5, suggest rename. Otherwise, ask user.

```
AskUserQuestion:
  question: "Column 'zip_code' exists in DB but not migrations. Column 'postal_code' exists in migrations but not DB. Is this a rename?"
  header: "Rename?"
  options:
    - label: "Yes, rename zip_code to postal_code (Recommended)"
      description: "Generate $table->renameColumn('zip_code', 'postal_code')"
    - label: "No, drop zip_code and add postal_code"
      description: "Data in zip_code will be lost"
    - label: "Keep both"
      description: "Don't modify either column"
```

**3.7 Index Differences**
- Missing indexes
- Extra indexes
- Index type mismatches

**3.8 Foreign Key Differences**
- Missing foreign keys
- Extra foreign keys
- Constraint name mismatches

### Step 4 - Generate Migration

Create a timestamped migration file:

```
database/migrations/YYYY_MM_DD_HHMMSS_schema_sync_fix.php
```

Structure:
```php
<?php

declare(strict_types=1);

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

/**
 * Schema Sync Migration
 *
 * Generated by /schema-sync on YYYY-MM-DD HH:MM:SS
 *
 * Changes:
 * - [connection:default] table 'users': add column 'middle_name' (string, nullable)
 * - [connection:uacs] table 'cities_municipalities': rename 'zip_code' to 'postal_code'
 * - ...
 */
return new class extends Migration
{
    public function up(): void
    {
        // Connection: default
        Schema::table('users', function (Blueprint $table) {
            $table->string('middle_name')->nullable()->after('first_name');
        });

        // Connection: uacs
        Schema::connection(config('database.module_connections.uacs'))
            ->table('cities_municipalities', function (Blueprint $table) {
                $table->renameColumn('zip_code', 'postal_code');
            });
    }

    public function down(): void
    {
        // Connection: default
        Schema::table('users', function (Blueprint $table) {
            $table->dropColumn('middle_name');
        });

        // Connection: uacs
        Schema::connection(config('database.module_connections.uacs'))
            ->table('cities_municipalities', function (Blueprint $table) {
                $table->renameColumn('postal_code', 'zip_code');
            });
    }
};
```

### Step 5 - Review and Apply

**5.1 Show Summary**

```
## Schema Sync Report

### Changes to Apply

| Connection | Table | Change | Details |
|------------|-------|--------|---------|
| default | users | ADD COLUMN | middle_name (string, nullable) |
| uacs | cities_municipalities | RENAME COLUMN | zip_code -> postal_code |

### Warnings

- Table 'telescope_entries' exists in DB but not in migrations (package table - ignored)
- Column 'legacy_id' in 'users' exists in DB but not in migrations

### Generated Migration

Path: database/migrations/2026_03_05_143022_schema_sync_fix.php
```

**5.2 Pre-Apply Confirmation**

```
AskUserQuestion:
  question: "Ready to apply? Has this been tested on staging?"
  header: "Apply"
  options:
    - label: "Yes, apply now"
      description: "Run php artisan migrate"
    - label: "Review migration file first"
      description: "I'll check the generated file and run manually"
    - label: "Cancel"
      description: "Don't apply, but keep the migration file"
```

**5.3 Apply**

If user confirms:
```bash
php artisan migrate
```

---

## Interactive Confirmations

### When to Ask

| Scenario | Action |
|----------|--------|
| Column rename detected (high confidence) | Inform, proceed |
| Column rename detected (low confidence) | Ask user |
| Extra column in DB (not in migrations) | Ask: keep, delete, or is it renamed? |
| Column type change that might lose data | Warn, ask confirmation |
| Dropping any column | Always ask |
| Dropping any table | Always ask, recommend manual review |
| Foreign key constraint removal | Warn about referential integrity |

### Confirmation Dialog Pattern

```
AskUserQuestion:
  question: "{Describe the situation and options}"
  header: "{Short label}"
  options:
    - label: "{Recommended option} (Recommended)"
      description: "{What this does}"
    - label: "{Alternative option}"
      description: "{What this does}"
    - label: "Skip this change"
      description: "Don't include in migration"
```

---

## Safety Rails

### Never Auto-Execute

1. **Always generate migration file** - never direct SQL
2. **Migration file can be reviewed** before running
3. **Migration can be rolled back** if issues arise

### Protected Patterns

1. **Never drop tables automatically** - always require explicit user confirmation
2. **Never drop columns with data** without warning about data loss
3. **Always generate rollback (down) method** in migration
4. **Warn about foreign key constraints** that might prevent changes

### Staging Check

Before applying in what might be production:

```
AskUserQuestion:
  question: "This appears to be a production database. Has this migration been tested on staging first?"
  header: "Production Warning"
  options:
    - label: "Yes, tested on staging"
      description: "Proceed with migration"
    - label: "No, this is staging"
      description: "Proceed with migration"
    - label: "Cancel"
      description: "I need to test on staging first"
```

Detection heuristics for production:
- Database name contains "prod" or "production"
- Environment is "production"
- Large number of rows in key tables

---

## Error Handling

### Connection Failures

If a database connection fails:
```
Unable to connect to database 'uacs'.
Check config/database.php and ensure the database exists.
Skipping this connection.
```

### Doctrine DBAL Not Installed

If doctrine/dbal is missing:
```bash
composer require doctrine/dbal
```

### Migration Parse Failures

If a migration file can't be parsed:
```
Warning: Could not parse migration '2025_01_01_000001_complex_migration.php'
This migration uses patterns that can't be statically analyzed.
Consider running a fresh migration on a test database for accurate comparison.
```

---

## Example Session

```
User: /schema-sync

Claude: Starting schema sync...

## Step 1: Discovering Database Connections

Found 3 connections:
- default (mysql)
- uacs (mysql) via module_connections.uacs
- logs (mysql) via module_connections.logs

## Step 2: Introspecting Current Schema

[default] Found 45 tables
[uacs] Found 12 tables
[logs] Found 3 tables

## Step 3: Parsing Migrations

Found 67 migration files
Parsed 65 successfully, 2 skipped (complex patterns)

## Step 4: Computing Diff

### Differences Found

| Connection | Table | Issue | Details |
|------------|-------|-------|---------|
| uacs | cities_municipalities | MISSING COLUMN | postal_code (string(10), nullable) |
| uacs | cities_municipalities | EXTRA COLUMN | zip_code (string(10), nullable) |

### Analysis

The columns 'zip_code' (DB) and 'postal_code' (migrations) have:
- Same type: string(10) ✓
- Same nullable: true ✓
- Same default: null ✓

This looks like a column rename.

[AskUserQuestion: Is this a rename?]

User: Yes, rename

Claude: Noted. Generating migration with rename...

## Generated Migration

Path: database/migrations/2026_03_05_150000_schema_sync_fix.php

Changes:
- [uacs] cities_municipalities: RENAME zip_code -> postal_code

[AskUserQuestion: Ready to apply?]

User: Yes, apply now

Claude: Running: php artisan migrate

Migrating: 2026_03_05_150000_schema_sync_fix
Migrated:  2026_03_05_150000_schema_sync_fix (45.23ms)

Schema sync complete!
```

---

## Limitations

1. **Complex migrations** - Migrations with conditionals, loops, or method calls may not parse correctly
2. **Dynamic column names** - Columns created via variables can't be detected
3. **Package migrations** - Tables from packages (Telescope, Horizon, etc.) will show as "extra"
4. **Data migrations** - This tool only handles schema, not data transformations

---

## Troubleshooting

### "Column type mismatch but they look the same"

Doctrine and Laravel may report types differently:
- Laravel `string` = Doctrine `string` = MySQL `varchar`
- Laravel `boolean` = Doctrine `boolean` = MySQL `tinyint(1)`

The tool normalizes these, but edge cases may occur.

### "Migration keeps detecting same diff"

The original migration may have been modified in-place instead of creating a new migration. This is common in early development. The schema sync migration will fix the drift.

### "Foreign key prevents column modification"

Drop and recreate foreign keys around the column change:
```php
$table->dropForeign(['column']);
$table->modifyColumn(...);
$table->foreign('column')->references('id')->on('other_table');
```

---

2. **Generated migrations stay in this repo** - they're project-specific fixes

---

## Files Modified

This skill only creates new files:
- `database/migrations/YYYY_MM_DD_HHMMSS_schema_sync_fix.php`

It never modifies existing files.
