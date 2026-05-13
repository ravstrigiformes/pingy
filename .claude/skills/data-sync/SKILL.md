---
name: data-sync
description: Synchronize reference/lookup data between seeders and production database - detect missing or outdated seed data
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, AskUserQuestion
user-invocable: true
---

# Data Sync

A skill for detecting and fixing seed data drift by comparing seeder definitions with actual database contents. Handles reference/lookup tables that should contain specific system data.

## Philosophy

- **Reference data only** - Only sync "system" data (lookup tables, constants), never user-generated data
- **Insert-safe by default** - Missing rows are safe to add
- **Update-cautious** - Changed rows require explicit confirmation
- **Delete-never** - Never auto-delete rows (they might be user-added)
- **Idempotent** - Running multiple times produces same result

---

## Invocation

### `/data-sync` (default - interactive)

Full interactive mode:
1. Discovers seedable tables (from seeder files)
2. Compares seeder data with database contents
3. Shows diff report
4. Asks for confirmation on updates
5. Executes sync via seeder or direct insert

### `/data-sync --dry-run`

Preview mode - shows what would change without making changes.

### `/data-sync --table={name}`

Target a specific table only.

### `/data-sync --seeder={class}`

Run a specific seeder class only.

### `/data-sync --force`

Skip confirmations (for CI/CD or scripted use).

---

## Seedable vs User Data

### Seedable Tables (Reference/Lookup Data)

These tables contain system-defined data that should be consistent across environments:

| Category | Tables | Identifier |
|----------|--------|------------|
| **Types/Classifications** | `payee_types`, `document_types`, `action_types` | `code` or `slug` |
| **Geographic** | `countries`, `regions`, `provinces`, `cities_municipalities`, `barangays` | `psgc_code` or `uacs_code` |
| **UACS Codes** | `fund_clusters`, `fund_sources`, `object_codes`, `geographic_classes` | `code` |
| **System Config** | `settings`, `permissions`, `abilities` | `key` or `name` |
| **Roles** | `roles`, `role_levels` | `code` or `slug` |

### User Data (Never Sync)

These tables contain user-generated data - never modify:

| Category | Tables |
|----------|--------|
| **Users** | `users`, `user_profiles`, `user_settings` |
| **Documents** | `documents`, `document_steps`, `lt_user_docstep` |
| **Transactions** | `checks`, `vouchers`, `disbursements` |
| **Files** | `files`, `file_versions` |
| **Audit** | `activity_log`, `audit_logs` |

---

## Step-by-Step Process

### Step 1 - Discover Seeders

Find all seeder files:
```
database/seeders/*.php
```

Parse each seeder to extract:
- Target table(s)
- Data being seeded
- Unique identifier column (for matching)

**Seeder Patterns to Detect:**

```php
// Pattern 1: Direct insert
DB::table('payee_types')->insert([
    ['code' => 'IND', 'name' => 'Individual'],
    ['code' => 'CORP', 'name' => 'Corporation'],
]);

// Pattern 2: Model create
PayeeType::create(['code' => 'IND', 'name' => 'Individual']);

// Pattern 3: Upsert
PayeeType::upsert($data, ['code'], ['name', 'description']);

// Pattern 4: firstOrCreate
PayeeType::firstOrCreate(['code' => 'IND'], ['name' => 'Individual']);

// Pattern 5: CSV/JSON import
$data = json_decode(file_get_contents('path/to/data.json'));
```

### Step 2 - Extract Seeder Data

For each seeder, extract the data that would be seeded:

```php
// Via tinker - simulate seeder without executing
// Parse the seeder file and extract data arrays

$seederData = [
    'payee_types' => [
        ['code' => 'IND', 'name' => 'Individual', 'description' => '...'],
        ['code' => 'CORP', 'name' => 'Corporation', 'description' => '...'],
        // ...
    ],
    'countries' => [
        ['code' => 'PH', 'name' => 'Philippines', 'iso_code_2' => 'PH'],
        // ...
    ],
];
```

### Step 3 - Fetch Current Database Data

For each seedable table, fetch current data:

```php
// Via tinker
$currentData = DB::table('payee_types')->get()->keyBy('code')->toArray();
```

### Step 4 - Compute Diff

Compare seeder data with database data:

**4.1 Identify Unique Key**

Each seedable table needs a unique identifier for matching:

| Table | Unique Key | Reason |
|-------|------------|--------|
| `payee_types` | `code` | Business identifier |
| `countries` | `code` or `iso_code_2` | ISO standard |
| `regions` | `psgc_code` | Government standard |
| `fund_clusters` | `code` | UACS standard |

**4.2 Diff Categories**

| Status | Description | Action |
|--------|-------------|--------|
| **MISSING** | In seeder, not in DB | INSERT (safe) |
| **MATCH** | In both, values identical | SKIP |
| **CHANGED** | In both, values differ | UPDATE (ask user) |
| **EXTRA** | In DB, not in seeder | IGNORE (user-added) |

**4.3 Change Detection**

For CHANGED rows, identify which columns differ:

```
Table: payee_types
Row: code='IND'

| Column | Seeder Value | DB Value | Action |
|--------|--------------|----------|--------|
| name | Individual | Individual | - |
| description | Natural person | A single person | UPDATE? |
```

### Step 5 - Generate Sync Plan

Create a detailed sync plan:

```
## Data Sync Plan

### payee_types (via PayeeTypeSeeder)

| Action | Code | Changes |
|--------|------|---------|
| INSERT | TRUST | New row: name='Trust Fund' |
| UPDATE | IND | description: 'A single person' -> 'Natural person' |
| SKIP | CORP | No changes |
| IGNORE | CUSTOM1 | Exists in DB only (user-added) |

### countries (via CountrySeeder)

| Action | Code | Changes |
|--------|------|---------|
| INSERT | SS | New row: name='South Sudan', iso_code_2='SS' |
| SKIP | PH | No changes |
```

### Step 6 - User Confirmation

**For INSERTS (safe):**
```
The following rows will be INSERTED:

| Table | Key | Data |
|-------|-----|------|
| payee_types | TRUST | name='Trust Fund', description='...' |
| countries | SS | name='South Sudan' |

Proceed with inserts?
```

**For UPDATES (needs confirmation):**
```
AskUserQuestion:
  question: "The following rows have CHANGED values. Update them?"
  header: "Updates"
  options:
    - label: "Update all changed rows"
      description: "Overwrite DB values with seeder values"
    - label: "Review each change"
      description: "I'll confirm each update individually"
    - label: "Skip updates"
      description: "Only insert missing rows, don't modify existing"
```

**For individual review:**
```
AskUserQuestion:
  question: "payee_types.IND: Update 'description' from 'A single person' to 'Natural person'?"
  header: "Update?"
  options:
    - label: "Yes, update"
      description: "Use seeder value"
    - label: "No, keep DB value"
      description: "Database value is correct"
    - label: "Skip all remaining updates"
      description: "Don't ask about other updates"
```

### Step 7 - Execute Sync

**Option A: Run Seeder (if idempotent)**

If the seeder uses `upsert` or `firstOrCreate`:
```bash
php artisan db:seed --class=PayeeTypeSeeder
```

**Option B: Direct SQL (for precise control)**

Generate and execute targeted SQL:
```php
// Inserts
DB::table('payee_types')->insert([
    ['code' => 'TRUST', 'name' => 'Trust Fund', ...],
]);

// Updates
DB::table('payee_types')
    ->where('code', 'IND')
    ->update(['description' => 'Natural person']);
```

**Option C: Generate Migration (for audit trail)**

Create a data migration:
```php
// database/migrations/YYYY_MM_DD_HHMMSS_data_sync_seed_data.php

public function up(): void
{
    // Insert missing payee_types
    DB::table('payee_types')->insert([
        ['code' => 'TRUST', 'name' => 'Trust Fund', 'created_at' => now()],
    ]);

    // Update changed payee_types
    DB::table('payee_types')
        ->where('code', 'IND')
        ->update(['description' => 'Natural person', 'updated_at' => now()]);
}
```

---

## Multi-Database Support

Like schema-sync, data-sync handles multiple connections:

```php
// Seeder specifies connection
Schema::connection(config('database.module_connections.uacs'))
    ->table('regions')
    ->insert($data);
```

The sync respects these connection specifications.

---

## Handling Large Datasets

For tables with thousands of rows (e.g., `barangays` with 42,000+ rows):

### Chunked Comparison

```php
// Compare in chunks of 1000
DB::table('barangays')
    ->orderBy('id')
    ->chunk(1000, function ($rows) use ($seederData) {
        // Compare chunk
    });
```

### Hash-Based Detection

For very large tables, use hash comparison first:

```php
// Quick check: has anything changed?
$dbHash = DB::table('barangays')->selectRaw('MD5(GROUP_CONCAT(psgc_code, name ORDER BY id))')->value('hash');
$seederHash = md5(collect($seederData)->sortBy('id')->map(fn($r) => $r['psgc_code'] . $r['name'])->join(''));

if ($dbHash === $seederHash) {
    // No changes, skip detailed comparison
}
```

### Progress Reporting

For long operations:
```
Comparing barangays... [████████████████████░░░░░░░░░░] 67% (28,000 / 42,000)
```

---

## Seeder Best Practices

For data-sync to work effectively, seeders should follow these patterns:

### Use Upsert for Idempotency

```php
// GOOD - can run multiple times safely
PayeeType::upsert(
    $data,
    ['code'],           // Unique key
    ['name', 'description']  // Columns to update
);

// BAD - fails on duplicate, can't update
PayeeType::insert($data);
```

### Define Unique Keys Clearly

```php
// GOOD - explicit unique identifier
$data = [
    ['code' => 'IND', 'name' => 'Individual'],  // 'code' is the key
];

// BAD - no clear identifier
$data = [
    ['name' => 'Individual'],  // How do we match this?
];
```

### Separate System vs Sample Data

```php
// SystemDataSeeder - always run in production
class PayeeTypeSeeder extends Seeder
{
    public function run(): void
    {
        // Essential reference data
    }
}

// SampleDataSeeder - only for development
class FakePayeeSeeder extends Seeder
{
    public function run(): void
    {
        // Test data, never in production
    }
}
```

---

## Configuration

### Seedable Tables Config

Create a config file to define which tables are seedable:

```php
// config/data-sync.php
return [
    'seedable_tables' => [
        'payee_types' => [
            'seeder' => PayeeTypeSeeder::class,
            'unique_key' => 'code',
            'connection' => 'default',
            'allow_updates' => true,
        ],
        'countries' => [
            'seeder' => CountrySeeder::class,
            'unique_key' => 'code',
            'connection' => 'default',
            'allow_updates' => true,
        ],
        'regions' => [
            'seeder' => RegionSeeder::class,
            'unique_key' => 'psgc_code',
            'connection' => 'uacs',
            'allow_updates' => false,  // Never update, only insert
        ],
        // ...
    ],

    'ignore_tables' => [
        'users',
        'documents',
        'password_reset_tokens',
        'sessions',
        'cache',
        'jobs',
        // ...
    ],
];
```

### Auto-Detection (if no config)

If no config exists, infer from seeder files:
1. Parse seeder to find target table
2. Look for `upsert` first argument for unique key
3. Fall back to 'code', 'slug', or 'id'

---

## Safety Rails

### Never Delete

Even if a row exists in DB but not in seeder, never delete it:
- Could be user-added data
- Could be from a newer seeder version
- Deletion is destructive and hard to undo

### Warn on Sensitive Tables

If user tries to sync a table that looks like user data:
```
Warning: 'users' looks like a user data table, not reference data.
Are you sure you want to sync this table?

[Yes, sync anyway] [No, skip this table]
```

### Production Warning

```
AskUserQuestion:
  question: "This appears to be a production database. Data sync will modify live data. Continue?"
  header: "Production"
  options:
    - label: "Yes, I understand"
      description: "Proceed with data sync"
    - label: "Generate SQL only"
      description: "Show me the SQL without executing"
    - label: "Cancel"
      description: "Don't modify production data"
```

---

## Example Session

```
User: /data-sync

Claude: Starting data sync...

## Step 1: Discovering Seeders

Found 12 seeders:
- PayeeTypeSeeder -> payee_types
- CountrySeeder -> countries
- RegionSeeder -> regions (uacs)
- ProvinceSeeder -> provinces (uacs)
- CityMunicipalitySeeder -> cities_municipalities (uacs)
- BarangaySeeder -> barangays (uacs)
- FundClusterSeeder -> fund_clusters (uacs)
- ...

## Step 2: Comparing Data

| Table | Connection | Seeder Rows | DB Rows | Missing | Changed | Extra |
|-------|------------|-------------|---------|---------|---------|-------|
| payee_types | default | 5 | 4 | 1 | 0 | 0 |
| countries | default | 195 | 195 | 0 | 0 | 0 |
| regions | uacs | 17 | 17 | 0 | 1 | 0 |
| cities_municipalities | uacs | 1,647 | 1,645 | 2 | 1 | 0 |

## Step 3: Sync Plan

### INSERTS (safe)

| Table | Key | Data |
|-------|-----|------|
| payee_types | TRUST | name='Trust Fund' |
| cities_municipalities | 012801 | name='New City 1' |
| cities_municipalities | 012802 | name='New City 2' |

### UPDATES (need confirmation)

| Table | Key | Column | Old Value | New Value |
|-------|-----|--------|-----------|-----------|
| regions | 130000000 | name | Metro Manila | National Capital Region |
| cities_municipalities | 137404 | postal_code | NULL | 1100 |

[AskUserQuestion: Proceed with inserts?]

User: Yes

[AskUserQuestion: Update changed rows?]

User: Review each

[AskUserQuestion: Update regions.130000000.name?]

User: Yes, update

[AskUserQuestion: Update cities_municipalities.137404.postal_code?]

User: Yes, update

Claude: Executing sync...

✓ Inserted 3 rows
✓ Updated 2 rows

Data sync complete!
```

---

## Integration with Schema Sync

Run both for complete database synchronization:

```bash
# First, fix schema drift
/schema-sync

# Then, fix data drift
/data-sync
```

Or suggest automatically:
```
Schema sync complete.
Would you like to run data sync to ensure seed data is up to date?
```

---

## Troubleshooting

### "Seeder uses dynamic data"

Some seeders generate data dynamically (e.g., using Faker). These can't be compared statically.

Solution: Mark these seeders as `skip_comparison: true` in config.

### "Can't determine unique key"

If a table has no clear unique identifier:

```
AskUserQuestion:
  question: "Table 'categories' has no obvious unique key. Which column should be used?"
  header: "Unique Key"
  options:
    - label: "id"
    - label: "slug"
    - label: "name"
    - label: "Skip this table"
```

### "Too many changes detected"

If more than 50% of rows are different, warn:
```
Warning: 823 of 1,647 rows in cities_municipalities are different.
This suggests the seeder data may have been significantly updated.
Consider running the full seeder instead of row-by-row sync.

[Run full seeder] [Continue with row-by-row] [Cancel]
```
