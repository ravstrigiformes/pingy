---
name: pest-testing
description: Testing PHP applications with Pest framework following TDD principles and Laravel testing best practices
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# Pest Testing Framework

This skill provides guidance on testing PHP applications with Pest, Laravel's recommended testing framework.

## Pest Fundamentals

### Why Pest
- Modern, expressive syntax with minimal boilerplate
- Built on PHPUnit - full compatibility
- Beautiful console output
- Built-in parallel testing
- Native code coverage
- Architectural testing

### Basic Syntax
```php
<?php

// Simple test
it('has a welcome page', function () {
    $response = $this->get('/');
    $response->assertStatus(200);
});

// Grouped tests
describe('User registration', function () {
    it('requires email', function () {
        // ...
    });

    it('requires password', function () {
        // ...
    });
});

// With test description
test('users can create products', function () {
    // ...
});
```

## Test Organization

### Directory Structure
```
tests/
├── Feature/
│   ├── Modules/
│   │   ├── Product/
│   │   │   ├── CreateProductTest.php
│   │   │   ├── UpdateProductTest.php
│   │   │   ├── DeleteProductTest.php
│   │   │   └── ListProductTest.php
│   │   └── Order/
│   │       └── ...
│   ├── Auth/
│   │   ├── LoginTest.php
│   │   └── RegistrationTest.php
│   └── ...
├── Unit/
│   ├── Services/
│   │   └── ProductServiceTest.php
│   ├── DTOs/
│   │   └── CreateProductDtoTest.php
│   └── ...
├── Pest.php
└── TestCase.php
```

### Pest.php Configuration
```php
<?php

use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

// Base test case for all tests
uses(TestCase::class)->in('Feature', 'Unit');

// Feature tests use RefreshDatabase
uses(RefreshDatabase::class)->in('Feature');

// Global helpers
function actingAsAdmin(): TestCase
{
    $admin = User::factory()->admin()->create();
    return test()->actingAs($admin);
}

function createProduct(array $attributes = []): Product
{
    return Product::factory()->create($attributes);
}
```

## Feature Tests

### API Endpoint Testing
```php
<?php

use App\Modules\Product\Models\Product;
use function Pest\Laravel\{getJson, postJson, putJson, deleteJson};

describe('Product API', function () {

    describe('GET /api/products', function () {
        it('returns paginated products', function () {
            Product::factory()->count(15)->create();

            getJson('/api/products')
                ->assertOk()
                ->assertJsonStructure([
                    'data' => [
                        '*' => ['id', 'name', 'price'],
                    ],
                    'links',
                    'meta',
                ]);
        });

        it('filters products by name', function () {
            Product::factory()->create(['name' => 'Widget']);
            Product::factory()->create(['name' => 'Gadget']);

            getJson('/api/products?name=Widget')
                ->assertOk()
                ->assertJsonCount(1, 'data')
                ->assertJsonPath('data.0.name', 'Widget');
        });
    });

    describe('POST /api/products', function () {
        it('creates a product with valid data', function () {
            $data = [
                'name' => 'New Product',
                'price' => 29.99,
                'description' => 'A great product',
            ];

            postJson('/api/products', $data)
                ->assertCreated()
                ->assertJsonPath('data.name', 'New Product');

            $this->assertDatabaseHas('products', ['name' => 'New Product']);
        });

        it('validates required fields', function () {
            postJson('/api/products', [])
                ->assertUnprocessable()
                ->assertJsonValidationErrors(['name', 'price']);
        });

        it('validates price is positive', function () {
            postJson('/api/products', [
                'name' => 'Product',
                'price' => -10,
            ])
                ->assertUnprocessable()
                ->assertJsonValidationErrors(['price']);
        });
    });

    describe('PUT /api/products/{id}', function () {
        it('updates an existing product', function () {
            $product = Product::factory()->create(['name' => 'Old Name']);

            putJson("/api/products/{$product->id}", ['name' => 'New Name'])
                ->assertOk()
                ->assertJsonPath('data.name', 'New Name');

            expect($product->fresh()->name)->toBe('New Name');
        });

        it('returns 404 for non-existent product', function () {
            putJson('/api/products/999999', ['name' => 'Name'])
                ->assertNotFound();
        });
    });

    describe('DELETE /api/products/{id}', function () {
        it('soft deletes a product', function () {
            $product = Product::factory()->create();

            deleteJson("/api/products/{$product->id}")
                ->assertOk();

            expect($product->fresh()->trashed())->toBeTrue();
            $this->assertSoftDeleted('products', ['id' => $product->id]);
        });
    });
});
```

### Authentication Testing
```php
<?php

use App\Models\User;

describe('Authentication', function () {
    it('requires authentication for protected routes', function () {
        getJson('/api/products')
            ->assertUnauthorized();
    });

    it('allows authenticated users', function () {
        $user = User::factory()->create();

        $this->actingAs($user)
            ->getJson('/api/products')
            ->assertOk();
    });

    it('validates user permissions', function () {
        $user = User::factory()->create();
        $product = Product::factory()->create();

        $this->actingAs($user)
            ->deleteJson("/api/products/{$product->id}")
            ->assertForbidden();
    });
});
```

## Unit Tests

### Service Testing
```php
<?php

use App\Modules\Product\Services\ProductService;
use App\Modules\Product\DataTransferObjects\CreateProductDto;
use App\Modules\Product\Models\Product;
use App\Exceptions\ModelEntityNotFoundException;

describe('ProductService', function () {
    beforeEach(function () {
        $this->service = app(ProductService::class);
    });

    describe('create', function () {
        it('creates a product from DTO', function () {
            $dto = new CreateProductDto(
                name: 'Test Product',
                price: 19.99,
                description: 'Test description',
            );

            $product = $this->service->create($dto);

            expect($product)
                ->toBeInstanceOf(Product::class)
                ->name->toBe('Test Product')
                ->price->toBe(19.99);
        });
    });

    describe('find', function () {
        it('returns product by ID', function () {
            $product = Product::factory()->create();

            $found = $this->service->find(
                new FindProductDto(id: $product->id)
            );

            expect($found->id)->toBe($product->id);
        });

        it('throws exception for non-existent product', function () {
            $this->service->find(new FindProductDto(id: 999999));
        })->throws(ModelEntityNotFoundException::class);
    });
});
```

### DTO Testing
```php
<?php

use App\Modules\Product\DataTransferObjects\CreateProductDto;
use Illuminate\Http\Request;

describe('CreateProductDto', function () {
    it('creates from array', function () {
        $dto = new CreateProductDto(
            name: 'Product',
            price: 10.00,
        );

        expect($dto)
            ->name->toBe('Product')
            ->price->toBe(10.00)
            ->description->toBeNull();
    });

    it('converts to array', function () {
        $dto = new CreateProductDto(
            name: 'Product',
            price: 10.00,
        );

        expect($dto->toArray())->toBe([
            'name' => 'Product',
            'price' => 10.00,
        ]);
    });

    it('creates from API request', function () {
        $request = Request::create('/api/products', 'POST', [
            'name' => 'Product',
            'price' => '10.00',
        ]);
        $request->setMethod('POST');

        // Mock validation
        $request->merge(['validated' => fn ($key) => $request->get($key)]);

        $dto = CreateProductDto::fromApiRequest($request);

        expect($dto->name)->toBe('Product');
    });
});
```

## Pest Expectations

### Common Expectations
```php
// Equality
expect($value)->toBe('exact');
expect($value)->toEqual('loose');
expect($value)->not->toBe('something');

// Types
expect($user)->toBeInstanceOf(User::class);
expect($items)->toBeArray();
expect($count)->toBeInt();
expect($price)->toBeFloat();
expect($name)->toBeString();
expect($active)->toBeBool();
expect($nullable)->toBeNull();

// Collections
expect($items)->toHaveCount(5);
expect($items)->toContain('item');
expect($array)->toHaveKey('key');
expect($array)->toMatchArray(['a' => 1, 'b' => 2]);

// Strings
expect($string)->toContain('substring');
expect($string)->toStartWith('prefix');
expect($string)->toEndWith('suffix');
expect($email)->toMatch('/^[\w\.-]+@[\w\.-]+\.\w+$/');

// Numbers
expect($count)->toBeGreaterThan(0);
expect($count)->toBeLessThan(100);
expect($count)->toBeBetween(1, 10);

// Boolean
expect($active)->toBeTrue();
expect($deleted)->toBeFalse();
expect($exists)->toBeTruthy();
expect($empty)->toBeFalsy();

// Chaining (fluent)
expect($user)
    ->toBeInstanceOf(User::class)
    ->name->toBe('John')
    ->email->toContain('@')
    ->age->toBeGreaterThan(18);
```

### Higher-Order Expectations
```php
// Each item in collection
expect($users)->each(function ($user) {
    $user->toBeInstanceOf(User::class)
        ->email->toContain('@');
});

// Sequence
expect($users)->sequence(
    fn ($user) => $user->name->toBe('First'),
    fn ($user) => $user->name->toBe('Second'),
);
```

## Database Testing

### Assertions
```php
// Record exists
$this->assertDatabaseHas('products', [
    'name' => 'Widget',
    'price' => 29.99,
]);

// Record doesn't exist
$this->assertDatabaseMissing('products', [
    'name' => 'Deleted Product',
]);

// Record count
$this->assertDatabaseCount('products', 5);

// Soft deleted
$this->assertSoftDeleted('products', ['id' => $id]);
$this->assertNotSoftDeleted('products', ['id' => $id]);
```

### Factories
```php
// In database/factories/ProductFactory.php
<?php

namespace Database\Factories;

use App\Modules\Product\Models\Product;
use Illuminate\Database\Eloquent\Factories\Factory;

class ProductFactory extends Factory
{
    protected $model = Product::class;

    public function definition(): array
    {
        return [
            'name' => $this->faker->productName(),
            'price' => $this->faker->randomFloat(2, 1, 1000),
            'description' => $this->faker->paragraph(),
            'sku' => $this->faker->unique()->ean13(),
            'quantity' => $this->faker->numberBetween(0, 100),
        ];
    }

    public function outOfStock(): static
    {
        return $this->state(['quantity' => 0]);
    }

    public function premium(): static
    {
        return $this->state(['price' => $this->faker->randomFloat(2, 500, 2000)]);
    }
}
```

## TDD Workflow (Red-Green-Refactor)

### 1. Red - Write Failing Test First
```php
it('calculates order total with discount', function () {
    $order = Order::factory()->create();
    $order->addItem(Product::factory()->create(['price' => 100]), quantity: 2);
    $order->applyDiscount(percent: 10);

    expect($order->total())->toBe(180.00); // 200 - 10%
});
```

### 2. Green - Minimal Implementation
```php
// Just enough code to make the test pass
public function total(): float
{
    $subtotal = $this->items->sum(fn ($item) => $item->price * $item->quantity);
    return $subtotal * (1 - $this->discount_percent / 100);
}
```

### 3. Refactor - Improve While Tests Pass
```php
// Extract, rename, optimize - tests protect you
public function subtotal(): float
{
    return $this->items->sum(fn ($item) => $item->lineTotal());
}

public function discountAmount(): float
{
    return $this->subtotal() * ($this->discount_percent / 100);
}

public function total(): float
{
    return $this->subtotal() - $this->discountAmount();
}
```

## Running Tests

### Commands
```bash
# Run all tests
php artisan test

# Run with Pest directly
./vendor/bin/pest

# Run specific file
php artisan test tests/Feature/Modules/Product/CreateProductTest.php

# Run specific test
php artisan test --filter="creates a product"

# Run in parallel
php artisan test --parallel

# With coverage
php artisan test --coverage
php artisan test --coverage --min=80

# Watch mode (requires pestphp/pest-plugin-watch)
./vendor/bin/pest --watch
```

### Configuration (phpunit.xml)
```xml
<env name="APP_ENV" value="testing"/>
<env name="DB_CONNECTION" value="sqlite"/>
<env name="DB_DATABASE" value=":memory:"/>
<env name="CACHE_DRIVER" value="array"/>
<env name="QUEUE_CONNECTION" value="sync"/>
<env name="SESSION_DRIVER" value="array"/>
```

## Best Practices

### Do
- Write tests before implementation (TDD)
- Use `RefreshDatabase` for feature tests
- Create factories for test data
- Test edge cases and error paths
- Use descriptive test names
- Group related tests with `describe()`
- Keep tests independent
- Mock external services

### Don't
- Share state between tests
- Test implementation details
- Write flaky tests
- Skip error scenario testing
- Make tests depend on execution order
- Use production database
- Ignore slow tests
