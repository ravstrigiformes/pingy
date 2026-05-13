---
name: laravel-architecture
description: Laravel architectural patterns including modular monolith, service layer, DTOs, Action classes, and enterprise-grade application structure
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# Laravel Architecture Patterns

This skill provides guidance on Laravel architectural patterns for scalable, maintainable applications.

## Modular Monolith Architecture

### Directory Structure
```
app/
├── DataTransferObjects/
│   ├── BaseDtos/
│   │   ├── BaseDto.php
│   │   └── BaseListDto.php
│   └── Interfaces/
│       ├── FromApiRequestInterface.php
│       └── FromApiRequestWithIdInterface.php
├── Events/
├── Exceptions/
├── Helpers/
├── Http/
│   ├── Controllers/
│   │   └── Controller.php
│   ├── Middleware/
│   └── Resources/
│       └── ApiResource.php
├── Jobs/
├── Listeners/
├── Models/
│   └── Traits/
└── Modules/
    └── {ModuleName}/
        ├── DataTransferObjects/
        ├── Http/
        │   ├── Controllers/api/
        │   ├── Requests/api/
        │   └── Resources/api/
        ├── Models/
        ├── Services/
        ├── Events/
        ├── Listeners/
        ├── Jobs/
        └── Policies/
```

### Module Independence
- Each module is self-contained with all related components
- Modules communicate via events or explicit service injection
- Shared code lives in `app/` base directories
- Cross-module dependencies should be minimal

## Data Transfer Objects (DTOs)

### Base DTO Class
```php
<?php

declare(strict_types=1);

namespace App\DataTransferObjects\BaseDtos;

use ReflectionClass;
use ReflectionProperty;

abstract readonly class BaseDto
{
    protected function copyWith(array $overrides): static
    {
        return new static(...[
            ...$this->toArray(),
            ...$overrides,
        ]);
    }

    public function toArray(): array
    {
        $reflection = new ReflectionClass($this);
        $props = [];

        foreach ($reflection->getProperties(ReflectionProperty::IS_PUBLIC) as $prop) {
            $props[$prop->getName()] = $this->{$prop->getName()};
        }

        return array_filter($props, fn ($v) => $v !== null);
    }
}
```

### DTO Interface for API Requests
```php
<?php

declare(strict_types=1);

namespace App\DataTransferObjects\Interfaces;

use Illuminate\Http\Request;

interface FromApiRequestInterface
{
    public static function fromApiRequest(Request $request): static;
}

interface FromApiRequestWithIdInterface
{
    public static function fromApiRequest(Request $request, int|string $id): static;
}
```

### Operation-Specific DTOs
```php
<?php

declare(strict_types=1);

namespace App\Modules\Product\DataTransferObjects;

use App\DataTransferObjects\BaseDtos\BaseDto;
use App\DataTransferObjects\Interfaces\FromApiRequestInterface;
use Illuminate\Http\Request;

readonly class CreateProductDto extends BaseDto implements FromApiRequestInterface
{
    public function __construct(
        public string $name,
        public float $price,
        public ?string $description = null,
        public ?string $sku = null,
        public int $quantity = 0,
    ) {}

    public static function fromApiRequest(Request $request): static
    {
        return new static(
            name: $request->validated('name'),
            price: (float) $request->validated('price'),
            description: $request->validated('description'),
            sku: $request->validated('sku'),
            quantity: (int) $request->validated('quantity', 0),
        );
    }
}
```

### DTO with ID (Update/Delete Operations)
```php
<?php

declare(strict_types=1);

namespace App\Modules\Product\DataTransferObjects;

use App\DataTransferObjects\BaseDtos\BaseDto;
use App\DataTransferObjects\Interfaces\FromApiRequestWithIdInterface;
use Illuminate\Http\Request;

readonly class UpdateProductDto extends BaseDto implements FromApiRequestWithIdInterface
{
    public function __construct(
        public int|string $id,
        public ?string $name = null,
        public ?float $price = null,
        public ?string $description = null,
    ) {}

    public static function fromApiRequest(Request $request, int|string $id): static
    {
        return new static(
            id: $id,
            name: $request->validated('name'),
            price: $request->validated('price') !== null
                ? (float) $request->validated('price')
                : null,
            description: $request->validated('description'),
        );
    }
}
```

### List DTO with Filtering/Sorting
```php
<?php

declare(strict_types=1);

namespace App\Modules\Product\DataTransferObjects;

use App\DataTransferObjects\BaseDtos\BaseListDto;
use App\DataTransferObjects\Interfaces\FromApiRequestInterface;
use Illuminate\Http\Request;

readonly class ListProductDto extends BaseListDto implements FromApiRequestInterface
{
    public function __construct(
        public ?string $search = null,
        public ?string $name = null,
        public ?string $category = null,
        public ?bool $include_deleted = null,
        public ?bool $only_trashed = null,
        public ?string $_sort = null,
        public ?string $_order = null,
    ) {}

    public static function fromApiRequest(Request $request): static
    {
        return new static(
            search: $request->query('search'),
            name: $request->query('name'),
            category: $request->query('category'),
            include_deleted: $request->boolean('include_deleted'),
            only_trashed: $request->boolean('only_trashed'),
            _sort: $request->query('_sort'),
            _order: $request->query('_order'),
        );
    }

    public function hasParams(): bool
    {
        return $this->search !== null
            || $this->name !== null
            || $this->category !== null
            || $this->include_deleted !== null
            || $this->only_trashed !== null;
    }
}
```

## Service Layer Pattern

### Service Class Structure
```php
<?php

declare(strict_types=1);

namespace App\Modules\Product\Services;

use App\Exceptions\ModelEntityNotFoundException;
use App\Modules\Product\DataTransferObjects\CreateProductDto;
use App\Modules\Product\DataTransferObjects\UpdateProductDto;
use App\Modules\Product\DataTransferObjects\DeleteProductDto;
use App\Modules\Product\DataTransferObjects\FindProductDto;
use App\Modules\Product\DataTransferObjects\ListProductDto;
use App\Modules\Product\Models\Product;
use Illuminate\Database\Eloquent\Collection;
use Illuminate\Database\Eloquent\SoftDeletes;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\DB;

class ProductService
{
    protected string $table;
    protected array $defaultRelations = [];
    protected int $cacheTtl = 3600;

    public function __construct()
    {
        $this->table = (new Product)->getTable();
    }

    // === CRUD Operations ===

    public function create(CreateProductDto $dto): Product
    {
        return DB::transaction(function () use ($dto): Product {
            $product = Product::create([
                'name' => $dto->name,
                'price' => $dto->price,
                'description' => $dto->description,
                'sku' => $dto->sku,
                'quantity' => $dto->quantity,
            ]);

            $this->invalidateCache();

            return $product->refresh()->load($this->defaultRelations);
        }, attempts: 5);
    }

    public function update(UpdateProductDto $dto): Product
    {
        return DB::transaction(function () use ($dto): Product {
            $product = $this->findModel($dto->id);

            $updates = array_filter([
                'name' => $dto->name,
                'price' => $dto->price,
                'description' => $dto->description,
            ], fn ($v) => $v !== null);

            if (!empty($updates)) {
                $product->update($updates);
            }

            $this->invalidateCache();

            return $product->refresh()->load($this->defaultRelations);
        }, attempts: 5);
    }

    public function delete(DeleteProductDto $dto): Product
    {
        $product = $this->findModel($dto->id);

        if ($dto->force) {
            return $this->hardDelete($dto);
        }

        // Idempotent: already trashed
        if ($product->trashed()) {
            return $product->load($this->defaultRelations);
        }

        return DB::transaction(function () use ($product): Product {
            $product->delete();
            $this->invalidateCache();
            return $product->refresh()->load($this->defaultRelations);
        }, attempts: 5);
    }

    public function find(FindProductDto $dto): Product
    {
        return $this->findModel($dto->id, $dto->include_deleted)
            ->load($this->defaultRelations);
    }

    public function list(ListProductDto $dto): Collection|array
    {
        // Implementation with filtering, sorting, soft delete handling
    }

    // === Helper Methods ===

    protected function findModel(int|string $id, bool $withTrashed = false): Product
    {
        $query = Product::query();

        if ($withTrashed && in_array(SoftDeletes::class, class_uses_recursive(Product::class))) {
            $query->withTrashed();
        }

        $product = $query->find($id);

        if (!$product) {
            throw new ModelEntityNotFoundException(Product::class, $id);
        }

        return $product;
    }

    protected function invalidateCache(): void
    {
        if (Cache::supportsTags()) {
            Cache::tags(['product'])->flush();
        }
    }
}
```

## Controller Pattern (Thin Controllers)

### API Controller Structure
```php
<?php

declare(strict_types=1);

namespace App\Modules\Product\Http\Controllers\api;

use App\Http\Controllers\Controller;
use App\Modules\Product\DataTransferObjects\CreateProductDto;
use App\Modules\Product\DataTransferObjects\DeleteProductDto;
use App\Modules\Product\DataTransferObjects\FindProductDto;
use App\Modules\Product\DataTransferObjects\ListProductDto;
use App\Modules\Product\DataTransferObjects\UpdateProductDto;
use App\Modules\Product\Http\Requests\api\DestroyProductRequest;
use App\Modules\Product\Http\Requests\api\IndexProductRequest;
use App\Modules\Product\Http\Requests\api\ShowProductRequest;
use App\Modules\Product\Http\Requests\api\StoreProductRequest;
use App\Modules\Product\Http\Requests\api\UpdateProductRequest;
use App\Modules\Product\Http\Resources\api\ProductCollection;
use App\Modules\Product\Http\Resources\api\ProductResource;
use App\Modules\Product\Services\ProductService;
use Illuminate\Http\Resources\Json\JsonResource;
use Illuminate\Http\Resources\Json\ResourceCollection;

class ProductController extends Controller
{
    public function __construct(
        protected ProductService $service
    ) {}

    public function index(IndexProductRequest $request): ResourceCollection
    {
        $products = $this->service->list(ListProductDto::fromApiRequest($request));
        return (new ProductCollection($products))->withContext('list_item');
    }

    public function store(StoreProductRequest $request): JsonResource
    {
        $product = $this->service->create(CreateProductDto::fromApiRequest($request));
        return ProductResource::make($product)->withContext('store');
    }

    public function show(ShowProductRequest $request, int|string $id): JsonResource
    {
        $product = $this->service->find(FindProductDto::fromApiRequest($request, $id));
        return ProductResource::make($product)->withContext('show');
    }

    public function update(UpdateProductRequest $request, int|string $id): JsonResource
    {
        $product = $this->service->update(UpdateProductDto::fromApiRequest($request, $id));
        return ProductResource::make($product)->withContext('update');
    }

    public function destroy(DestroyProductRequest $request, int|string $id): JsonResource
    {
        $product = $this->service->delete(DeleteProductDto::fromApiRequest($request, $id));
        return ProductResource::make($product)->withContext('destroy');
    }
}
```

## Form Request Validation

### Request Class Structure
```php
<?php

declare(strict_types=1);

namespace App\Modules\Product\Http\Requests\api;

use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;

class StoreProductRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true; // Or check policies
    }

    public function rules(): array
    {
        return [
            'name' => ['required', 'string', 'max:255'],
            'price' => ['required', 'numeric', 'min:0'],
            'description' => ['nullable', 'string', 'max:1000'],
            'sku' => ['nullable', 'string', 'max:50', 'unique:products,sku'],
            'quantity' => ['nullable', 'integer', 'min:0'],
        ];
    }

    public function messages(): array
    {
        return [
            'name.required' => 'Product name is required.',
            'price.min' => 'Price cannot be negative.',
        ];
    }
}
```

## API Resources

### Resource with Context
```php
<?php

declare(strict_types=1);

namespace App\Modules\Product\Http\Resources\api;

use App\Http\Resources\ApiResource;

class ProductResource extends ApiResource
{
    public function toArray($request): array
    {
        $context = $this->getContext();

        $base = [
            'id' => $this->id,
            'name' => $this->name,
            'price' => $this->price,
        ];

        // Minimal for list views
        if ($context === 'list_item') {
            return $base;
        }

        // Full details for show/store/update
        return [
            ...$base,
            'description' => $this->description,
            'sku' => $this->sku,
            'quantity' => $this->quantity,
            'created_at' => $this->created_at?->toISOString(),
            'updated_at' => $this->updated_at?->toISOString(),
            'deleted_at' => $this->when(
                $this->deleted_at !== null,
                fn () => $this->deleted_at?->toISOString()
            ),
        ];
    }
}
```

## Event-Driven Communication

### Domain Events
```php
<?php

declare(strict_types=1);

namespace App\Modules\Order\Events;

use App\Modules\Order\Models\Order;
use Illuminate\Foundation\Events\Dispatchable;
use Illuminate\Queue\SerializesModels;

class OrderPlaced
{
    use Dispatchable, SerializesModels;

    public function __construct(
        public readonly Order $order
    ) {}
}
```

### Event Listeners
```php
<?php

declare(strict_types=1);

namespace App\Modules\Inventory\Listeners;

use App\Modules\Order\Events\OrderPlaced;
use App\Modules\Inventory\Services\InventoryService;

class ReserveInventory
{
    public function __construct(
        protected InventoryService $inventory
    ) {}

    public function handle(OrderPlaced $event): void
    {
        foreach ($event->order->items as $item) {
            $this->inventory->reserve($item->product_id, $item->quantity);
        }
    }
}
```

## Layer Responsibilities

| Layer | Responsibility | Should NOT |
|-------|---------------|------------|
| **Controller** | HTTP handling, request/response orchestration | Contain business logic, direct DB queries |
| **Form Request** | Validation rules, authorization checks | Transform data, call services |
| **DTO** | Data transfer between layers, immutable data | Contain logic, validate data |
| **Service** | Business logic, transactions, coordination | Handle HTTP, format responses |
| **Model** | Data representation, relationships, scopes | Contain business rules |
| **Resource** | Response formatting, context-aware output | Process business logic |
| **Event** | Cross-module communication, decoupling | Execute business logic directly |
| **Listener** | React to events, trigger side effects | Return responses |
