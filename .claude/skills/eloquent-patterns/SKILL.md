---
name: eloquent-patterns
description: Eloquent ORM patterns, query optimization, relationships, and database best practices for Laravel applications
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# Eloquent ORM Patterns

This skill provides guidance on Eloquent ORM patterns, query optimization, and database best practices.

## Model Structure

### Base Model Setup
```php
<?php

declare(strict_types=1);

namespace App\Modules\Product\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\SoftDeletes;
use Illuminate\Database\Eloquent\Builder;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

class Product extends Model
{
    use HasFactory, SoftDeletes;

    protected $fillable = [
        'name',
        'price',
        'description',
        'category_id',
        'quantity',
    ];

    protected $casts = [
        'price' => 'decimal:2',
        'quantity' => 'integer',
        'metadata' => 'array',
        'is_active' => 'boolean',
        'published_at' => 'datetime',
    ];

    protected $attributes = [
        'quantity' => 0,
        'is_active' => true,
    ];

    // === Relationships ===

    public function category(): BelongsTo
    {
        return $this->belongsTo(Category::class);
    }

    public function orderItems(): HasMany
    {
        return $this->hasMany(OrderItem::class);
    }

    // === Scopes ===

    public function scopeActive(Builder $query): Builder
    {
        return $query->where('is_active', true);
    }

    public function scopeInStock(Builder $query): Builder
    {
        return $query->where('quantity', '>', 0);
    }

    public function scopeByCategory(Builder $query, int $categoryId): Builder
    {
        return $query->where('category_id', $categoryId);
    }

    public function scopeSearch(Builder $query, string $term): Builder
    {
        return $query->where(function (Builder $q) use ($term) {
            $q->where('name', 'LIKE', "%{$term}%")
              ->orWhere('description', 'LIKE', "%{$term}%");
        });
    }

    public function scopeOrdered(Builder $query, string $column = 'name', string $direction = 'asc'): Builder
    {
        $allowed = ['name', 'price', 'created_at', 'quantity'];
        $column = in_array($column, $allowed, true) ? $column : 'name';
        $direction = in_array(strtolower($direction), ['asc', 'desc'], true) ? $direction : 'asc';

        return $query->orderBy($column, $direction);
    }

    // === Accessors & Mutators ===

    protected function formattedPrice(): Attribute
    {
        return Attribute::make(
            get: fn () => '$' . number_format($this->price, 2),
        );
    }

    protected function name(): Attribute
    {
        return Attribute::make(
            set: fn (string $value) => trim($value),
        );
    }

    // === Business Logic ===

    public function isAvailable(): bool
    {
        return $this->is_active && $this->quantity > 0;
    }

    public function decrementStock(int $amount): bool
    {
        if ($this->quantity < $amount) {
            return false;
        }

        $this->decrement('quantity', $amount);
        return true;
    }
}
```

## Relationships

### One-to-One
```php
// User has one Profile
public function profile(): HasOne
{
    return $this->hasOne(Profile::class);
}

// Profile belongs to User
public function user(): BelongsTo
{
    return $this->belongsTo(User::class);
}
```

### One-to-Many
```php
// Category has many Products
public function products(): HasMany
{
    return $this->hasMany(Product::class);
}

// Product belongs to Category
public function category(): BelongsTo
{
    return $this->belongsTo(Category::class);
}
```

### Many-to-Many
```php
// Product belongs to many Tags
public function tags(): BelongsToMany
{
    return $this->belongsToMany(Tag::class)
        ->withPivot('order')
        ->withTimestamps();
}

// With custom pivot model
public function tags(): BelongsToMany
{
    return $this->belongsToMany(Tag::class)
        ->using(ProductTag::class)
        ->withPivot(['order', 'is_primary']);
}
```

### Has Many Through
```php
// Country has many Posts through Users
public function posts(): HasManyThrough
{
    return $this->hasManyThrough(Post::class, User::class);
}
```

### Polymorphic Relations
```php
// Commentable (Post, Video can have comments)
public function comments(): MorphMany
{
    return $this->morphMany(Comment::class, 'commentable');
}

// Comment belongs to commentable
public function commentable(): MorphTo
{
    return $this->morphTo();
}
```

## Query Optimization

### N+1 Problem Prevention

```php
// Bad - N+1 queries
$products = Product::all();
foreach ($products as $product) {
    echo $product->category->name; // Query per product
}

// Good - Eager loading
$products = Product::with('category')->get();
foreach ($products as $product) {
    echo $product->category->name; // No additional queries
}

// Nested eager loading
$orders = Order::with([
    'user',
    'items.product.category',
    'items.product.tags',
])->get();

// Conditional eager loading
$products = Product::with(['category' => function ($query) {
    $query->where('is_active', true);
}])->get();

// Lazy eager loading (when needed later)
$products = Product::all();
$products->load('category');
```

### Select Only Needed Columns
```php
// Bad - selecting all columns
$products = Product::with('category')->get();

// Good - selecting specific columns
$products = Product::select(['id', 'name', 'price', 'category_id'])
    ->with('category:id,name')
    ->get();
```

### Chunking Large Datasets
```php
// Process in batches to reduce memory usage
Product::chunk(500, function ($products) {
    foreach ($products as $product) {
        // Process each product
    }
});

// With cursor for even lower memory (one model at a time)
foreach (Product::cursor() as $product) {
    // Process each product
}

// Lazy collection (chunk under the hood)
Product::lazy()->each(function ($product) {
    // Process
});
```

### Efficient Counting
```php
// Bad - loads all records
$count = Product::all()->count();

// Good - SQL COUNT
$count = Product::count();

// Count with conditions
$count = Product::where('is_active', true)->count();

// Count related models without loading
$productsWithCount = Category::withCount('products')->get();
// Access via: $category->products_count
```

### Subqueries
```php
// Add computed column from related table
$users = User::addSelect([
    'last_order_at' => Order::select('created_at')
        ->whereColumn('user_id', 'users.id')
        ->latest()
        ->limit(1),
])->get();

// Order by subquery
$users = User::orderByDesc(
    Order::select('created_at')
        ->whereColumn('user_id', 'users.id')
        ->latest()
        ->limit(1)
)->get();
```

## Query Builder Patterns

### Dynamic Filtering
```php
public function buildListQuery(ListProductDto $dto): Builder
{
    return Product::query()
        ->when($dto->search, fn ($q, $search) =>
            $q->search($search)
        )
        ->when($dto->category_id, fn ($q, $categoryId) =>
            $q->where('category_id', $categoryId)
        )
        ->when($dto->min_price, fn ($q, $min) =>
            $q->where('price', '>=', $min)
        )
        ->when($dto->max_price, fn ($q, $max) =>
            $q->where('price', '<=', $max)
        )
        ->when($dto->only_trashed, fn ($q) =>
            $q->onlyTrashed()
        )
        ->when($dto->include_deleted && !$dto->only_trashed, fn ($q) =>
            $q->withTrashed()
        )
        ->when($dto->_sort, fn ($q) =>
            $q->ordered($dto->_sort, $dto->_order ?? 'asc'),
            fn ($q) => $q->ordered('name', 'asc')
        );
}
```

### Raw Expressions (When Needed)
```php
// Computed columns
$products = Product::select([
    '*',
    DB::raw('price * quantity as total_value'),
])->get();

// Complex aggregations
$stats = DB::table('orders')
    ->select([
        DB::raw('DATE(created_at) as date'),
        DB::raw('COUNT(*) as order_count'),
        DB::raw('SUM(total) as revenue'),
    ])
    ->groupBy(DB::raw('DATE(created_at)'))
    ->get();
```

## Transactions

### Basic Transaction
```php
use Illuminate\Support\Facades\DB;

$order = DB::transaction(function () use ($dto) {
    $order = Order::create([...]);

    foreach ($dto->items as $item) {
        $order->items()->create($item);
        Product::find($item->product_id)->decrement('quantity', $item->quantity);
    }

    return $order;
});
```

### Transaction with Retries
```php
// Automatic retry on deadlock
$result = DB::transaction(function () use ($dto) {
    // Operations that might deadlock
    return $this->processOrder($dto);
}, attempts: 5);
```

### Manual Transaction Control
```php
DB::beginTransaction();

try {
    $order = Order::create([...]);
    $this->processPayment($order);

    DB::commit();
} catch (\Exception $e) {
    DB::rollBack();
    throw $e;
}
```

## Soft Deletes

### Model Setup
```php
use Illuminate\Database\Eloquent\SoftDeletes;

class Product extends Model
{
    use SoftDeletes;

    // Include trashed in specific relationships
    public function orders(): HasMany
    {
        return $this->hasMany(Order::class)->withTrashed();
    }
}
```

### Querying
```php
// Only active records (default)
$products = Product::all();

// Include soft deleted
$products = Product::withTrashed()->get();

// Only soft deleted
$products = Product::onlyTrashed()->get();

// Check if trashed
if ($product->trashed()) {
    // ...
}

// Restore
$product->restore();

// Permanent delete
$product->forceDelete();
```

## Caching Strategies

### Query Caching
```php
use Illuminate\Support\Facades\Cache;

public function getActiveCategories(): Collection
{
    return Cache::tags(['categories'])
        ->remember('categories:active', 3600, function () {
            return Category::active()
                ->with('products:id,name,category_id')
                ->get();
        });
}

public function invalidateCategoryCache(): void
{
    Cache::tags(['categories'])->flush();
}
```

### Model Observer for Cache Invalidation
```php
class CategoryObserver
{
    public function saved(Category $category): void
    {
        Cache::tags(['categories'])->flush();
    }

    public function deleted(Category $category): void
    {
        Cache::tags(['categories'])->flush();
    }
}
```

## Migration Best Practices

### Schema Structure
```php
Schema::create('products', function (Blueprint $table) {
    $table->id();
    $table->ulid('ulid')->unique();

    // Foreign keys
    $table->foreignId('category_id')
        ->constrained()
        ->cascadeOnDelete();

    $table->foreignId('created_by')
        ->nullable()
        ->constrained('users')
        ->nullOnDelete();

    // Fields
    $table->string('name');
    $table->decimal('price', 10, 2);
    $table->text('description')->nullable();
    $table->unsignedInteger('quantity')->default(0);
    $table->boolean('is_active')->default(true);
    $table->json('metadata')->nullable();
    $table->timestamp('published_at')->nullable();

    // Timestamps and soft deletes
    $table->timestamps();
    $table->softDeletes();

    // Indexes
    $table->index('name');
    $table->index(['category_id', 'is_active']);
    $table->index('created_at');
});
```

### Index Strategy
```php
// Single column indexes for:
// - Foreign keys (automatic in some DBs)
// - Frequently filtered columns
// - Columns used in ORDER BY

// Composite indexes for:
// - Frequently combined WHERE clauses
// - ORDER BY with WHERE
$table->index(['status', 'created_at']);

// Unique indexes for:
// - Business-unique fields
$table->unique('email');
$table->unique(['tenant_id', 'slug']);
```

## Events and Observers

### Model Events
```php
class Product extends Model
{
    protected static function booted(): void
    {
        static::creating(function (Product $product) {
            $product->ulid = (string) Str::ulid();
        });

        static::updated(function (Product $product) {
            if ($product->wasChanged('price')) {
                event(new ProductPriceChanged($product));
            }
        });
    }
}
```

### Observer Pattern
```php
class ProductObserver
{
    public function creating(Product $product): void
    {
        $product->created_by = auth()->id();
    }

    public function created(Product $product): void
    {
        Cache::tags(['products'])->flush();
        event(new ProductCreated($product));
    }

    public function updated(Product $product): void
    {
        Cache::tags(['products'])->flush();
    }

    public function deleted(Product $product): void
    {
        Cache::tags(['products'])->flush();
    }
}

// Register in AppServiceProvider
Product::observe(ProductObserver::class);
```

## Performance Tips

### Avoid
```php
// Avoid: Loading all then filtering
$activeProducts = Product::all()->filter(fn ($p) => $p->is_active);

// Avoid: Counting loaded collection
$count = Product::all()->count();

// Avoid: N+1 in accessors
protected function categoryName(): Attribute
{
    return Attribute::make(
        get: fn () => $this->category->name, // N+1!
    );
}

// Avoid: Unnecessary hydration
$names = Product::all()->pluck('name');
```

### Prefer
```php
// Use: Database filtering
$activeProducts = Product::where('is_active', true)->get();

// Use: Database count
$count = Product::count();

// Use: Eager load before accessing
$products = Product::with('category')->get();
foreach ($products as $product) {
    echo $product->category->name;
}

// Use: Pluck directly
$names = Product::pluck('name');

// Use: Query builder for simple data
$names = DB::table('products')->pluck('name');
```

## Common Patterns

### Upsert (Insert or Update)
```php
Product::upsert(
    [
        ['sku' => 'ABC123', 'name' => 'Product A', 'price' => 10],
        ['sku' => 'DEF456', 'name' => 'Product B', 'price' => 20],
    ],
    ['sku'],              // Unique columns to check
    ['name', 'price']     // Columns to update if exists
);
```

### First or Create
```php
$category = Category::firstOrCreate(
    ['slug' => 'electronics'],           // Search criteria
    ['name' => 'Electronics']            // Values if creating
);

$product = Product::updateOrCreate(
    ['sku' => $dto->sku],
    [
        'name' => $dto->name,
        'price' => $dto->price,
    ]
);
```

### Batch Updates
```php
// Update multiple records
Product::where('category_id', $oldCategoryId)
    ->update(['category_id' => $newCategoryId]);

// Increment/Decrement
Product::where('id', $productId)->increment('view_count');
Product::where('id', $productId)->decrement('quantity', 5);
```
