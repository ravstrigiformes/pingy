---
name: php-coding-standards
description: PHP coding standards and conventions following PSR-12, modern PHP 8.2+, and enterprise-grade practices
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# PHP Coding Standards

This skill provides guidance on PHP coding standards for enterprise-grade applications.

## PSR-12 Extended Coding Style

### File Structure
```php
<?php

declare(strict_types=1);

namespace App\Namespace;

use ExternalClass;
use AnotherClass;

use function some_function;

use const SOME_CONSTANT;
```

### Strict Types Declaration
Every PHP file MUST start with strict types declaration:
```php
<?php

declare(strict_types=1);
```

### Type Declarations
- ALL parameters MUST have type declarations
- ALL return types MUST be declared (including `void`)
- ALL class properties MUST have type declarations
- Use union types when appropriate: `string|int`
- Use nullable types sparingly: `?string` or `string|null`
- Use `mixed` only when truly necessary

### Constructor Property Promotion (PHP 8.0+)
```php
// Preferred
public function __construct(
    private readonly string $name,
    private readonly int $age,
    private ?string $email = null,
) {}

// Avoid
private string $name;
private int $age;

public function __construct(string $name, int $age)
{
    $this->name = $name;
    $this->age = $age;
}
```

### Readonly Classes and Properties (PHP 8.2+)
```php
// Immutable DTOs
readonly class UserData
{
    public function __construct(
        public string $name,
        public string $email,
    ) {}
}

// Individual readonly properties
class Service
{
    public function __construct(
        private readonly Repository $repository,
    ) {}
}
```

### Enums (PHP 8.1+)
```php
enum Status: string
{
    case Pending = 'pending';
    case Active = 'active';
    case Inactive = 'inactive';

    public function label(): string
    {
        return match($this) {
            self::Pending => 'Pending Approval',
            self::Active => 'Active',
            self::Inactive => 'Inactive',
        };
    }
}
```

### Named Arguments
```php
// Use for clarity with multiple parameters
$user = new User(
    name: $name,
    email: $email,
    role: Role::Admin,
);

// Use for optional parameters
$this->notify(
    message: 'Hello',
    priority: Priority::High,
);
```

### Match Expressions
```php
// Preferred over switch for value returns
$result = match($status) {
    'pending' => 'Awaiting review',
    'approved' => 'Approved',
    'rejected' => 'Rejected',
    default => 'Unknown',
};
```

### Null Coalescing and Nullsafe Operators
```php
// Null coalescing
$name = $user->name ?? 'Guest';
$value ??= 'default';

// Nullsafe operator
$country = $user?->address?->country?->name;
```

## Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `UserService`, `OrderRepository` |
| Interfaces | PascalCase + suffix | `PaymentGatewayInterface` |
| Traits | PascalCase | `HasTimestamps`, `Searchable` |
| Methods | camelCase | `findById()`, `processOrder()` |
| Functions | snake_case | `array_map()`, `str_replace()` |
| Variables | camelCase | `$userId`, `$orderTotal` |
| Constants | SCREAMING_SNAKE | `MAX_RETRIES`, `API_VERSION` |
| Properties | camelCase | `$createdAt`, `$isActive` |

### Method Naming Patterns
```php
// Queries (return data)
findById(int $id): ?Entity
findByEmail(string $email): ?Entity
getAll(): Collection
listActive(): array

// Commands (perform actions)
create(CreateDto $dto): Entity
update(UpdateDto $dto): Entity
delete(DeleteDto $dto): bool
process(ProcessDto $dto): void

// Boolean checks
isActive(): bool
hasPermission(string $permission): bool
canAccess(Resource $resource): bool

// Transformations
toArray(): array
toString(): string
toDto(): DataTransferObject
```

## Documentation Standards

### When to Document
- Public API methods
- Complex algorithms
- Non-obvious business logic
- Workarounds and technical debt

### When NOT to Document
- Self-explanatory methods
- Simple getters/setters
- Code that follows clear patterns

### PHPDoc Format
```php
/**
 * Process a payment transaction.
 *
 * @param PaymentDto $dto Payment details
 * @throws PaymentFailedException When gateway rejects transaction
 * @throws InsufficientFundsException When account balance is too low
 */
public function process(PaymentDto $dto): PaymentResult
{
    // Implementation
}
```

## Code Quality Tools

### Laravel Pint (PSR-12)
```bash
# Check code style
./vendor/bin/pint --test

# Fix code style
./vendor/bin/pint

# Fix specific path
./vendor/bin/pint app/Modules/User
```

### PHPStan (Static Analysis)
```bash
# Run analysis
./vendor/bin/phpstan analyse

# Specific level (0-9)
./vendor/bin/phpstan analyse --level=8
```

### Rector (Automated Refactoring)
```bash
# Preview changes
./vendor/bin/rector process --dry-run

# Apply changes
./vendor/bin/rector process
```

## Anti-Patterns to Avoid

### Avoid Magic Methods Abuse
```php
// Bad - magic getter
public function __get($name) { ... }

// Good - explicit methods
public function getName(): string { ... }
```

### Avoid Array-Based Configuration
```php
// Bad
$config = ['host' => 'localhost', 'port' => 3306];

// Good
readonly class DatabaseConfig
{
    public function __construct(
        public string $host,
        public int $port,
    ) {}
}
```

### Avoid Stringly-Typed Code
```php
// Bad
function setStatus(string $status): void { ... }
setStatus('actve'); // Typo goes unnoticed

// Good
function setStatus(Status $status): void { ... }
setStatus(Status::Active); // Type-safe
```

### Avoid Boolean Parameters
```php
// Bad
function getUsers(bool $includeInactive): array { ... }

// Good
function getActiveUsers(): array { ... }
function getAllUsers(): array { ... }
// Or use an enum/options object
```
