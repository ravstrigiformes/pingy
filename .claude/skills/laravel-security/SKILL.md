---
name: laravel-security
description: Laravel security best practices following OWASP guidelines, authentication, authorization, and secure coding patterns
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# Laravel Security Best Practices

This skill provides guidance on securing Laravel applications following OWASP guidelines and enterprise security standards.

## OWASP Top 10 Protection

### 1. Broken Access Control (A01:2021)

#### Use Policies for Authorization
```php
<?php

namespace App\Modules\Product\Policies;

use App\Models\User;
use App\Modules\Product\Models\Product;

class ProductPolicy
{
    public function view(User $user, Product $product): bool
    {
        return $user->id === $product->user_id
            || $user->hasPermission('products.view-all');
    }

    public function update(User $user, Product $product): bool
    {
        return $user->id === $product->user_id
            || $user->hasRole('admin');
    }

    public function delete(User $user, Product $product): bool
    {
        return $user->hasRole('admin');
    }
}
```

#### Apply Policies in Controllers
```php
public function update(UpdateProductRequest $request, Product $product): JsonResource
{
    $this->authorize('update', $product);
    // ...
}
```

#### Prevent BOLA/IDOR
```php
// Bad - vulnerable to IDOR
$product = Product::find($id);

// Good - scoped to user
$product = $request->user()
    ->products()
    ->findOrFail($id);

// Good - using policy
$product = Product::findOrFail($id);
$this->authorize('view', $product);
```

### 2. Cryptographic Failures (A02:2021)

#### Password Hashing
```php
// Laravel handles this automatically with bcrypt
$user->password = Hash::make($request->password);

// Verify password
if (Hash::check($request->password, $user->password)) {
    // Valid
}

// Configure in config/hashing.php
'driver' => 'bcrypt',
'bcrypt' => [
    'rounds' => env('BCRYPT_ROUNDS', 12),
],
```

#### Encryption
```php
// Encrypt sensitive data
$encrypted = Crypt::encryptString($sensitiveData);
$decrypted = Crypt::decryptString($encrypted);

// Model attribute encryption
protected $casts = [
    'ssn' => 'encrypted',
    'api_key' => 'encrypted:array',
];
```

#### Secure Environment Configuration
```env
# Never commit these to version control
APP_KEY=base64:...
DB_PASSWORD=...
API_SECRET=...

# Use strong, unique keys
# Generate with: php artisan key:generate
```

### 3. Injection (A03:2021)

#### SQL Injection Prevention
```php
// Good - Eloquent (parameterized)
$users = User::where('email', $email)->get();

// Good - Query Builder (parameterized)
$users = DB::table('users')
    ->where('email', '=', $email)
    ->get();

// Good - Raw with bindings
$users = DB::select(
    'SELECT * FROM users WHERE email = ?',
    [$email]
);

// Bad - Never do this
$users = DB::select("SELECT * FROM users WHERE email = '$email'");
```

#### Command Injection Prevention
```php
// Good - escapeshellarg
$output = shell_exec('ls ' . escapeshellarg($userInput));

// Better - avoid shell execution entirely
$files = Storage::files($directory);

// Best - use Laravel's process helper (Laravel 10+)
use Illuminate\Support\Facades\Process;

$result = Process::run(['ls', '-la', $directory]);
```

### 4. Insecure Design (A04:2021)

#### Rate Limiting
```php
// In routes/api.php
Route::middleware('throttle:60,1')->group(function () {
    Route::get('/products', [ProductController::class, 'index']);
});

// Custom rate limiter in AppServiceProvider
RateLimiter::for('api', function (Request $request) {
    return Limit::perMinute(60)->by($request->user()?->id ?: $request->ip());
});

// Per-second rate limiting (Laravel 11+)
RateLimiter::for('uploads', function (Request $request) {
    return Limit::perSecond(1)->by($request->user()->id);
});
```

#### Input Validation
```php
class StoreProductRequest extends FormRequest
{
    public function rules(): array
    {
        return [
            'name' => ['required', 'string', 'max:255'],
            'price' => ['required', 'numeric', 'min:0', 'max:999999.99'],
            'description' => ['nullable', 'string', 'max:5000'],
            'category_id' => ['required', 'exists:categories,id'],
            'image' => ['nullable', 'image', 'mimes:jpg,png,webp', 'max:2048'],
        ];
    }
}
```

### 5. Security Misconfiguration (A05:2021)

#### Production Configuration
```php
// config/app.php - ensure in production:
'debug' => env('APP_DEBUG', false),
'env' => env('APP_ENV', 'production'),

// Verify .env in production
APP_DEBUG=false
APP_ENV=production
```

#### Security Headers
```php
// In middleware or global
public function handle($request, Closure $next)
{
    $response = $next($request);

    return $response
        ->header('X-Content-Type-Options', 'nosniff')
        ->header('X-Frame-Options', 'DENY')
        ->header('X-XSS-Protection', '1; mode=block')
        ->header('Strict-Transport-Security', 'max-age=31536000; includeSubDomains')
        ->header('Content-Security-Policy', "default-src 'self'");
}
```

### 6. Vulnerable Components (A06:2021)

#### Dependency Management
```bash
# Check for vulnerabilities
composer audit

# Update dependencies regularly
composer update

# Check outdated packages
composer outdated

# Security advisories
composer require --dev enlightn/security-checker
```

### 7. Authentication Failures (A07:2021)

#### Laravel Sanctum for APIs
```php
// Install
composer require laravel/sanctum

// Configure in config/sanctum.php
'stateful' => explode(',', env('SANCTUM_STATEFUL_DOMAINS', 'localhost')),

// Token creation
$token = $user->createToken('api-token', ['products:read']);

// Token with abilities
$user->createToken('admin-token', ['*']); // All abilities
$user->createToken('read-token', ['products:read', 'orders:read']);

// Check abilities
if ($user->tokenCan('products:write')) {
    // Authorized
}
```

#### Session Security
```php
// config/session.php
'lifetime' => env('SESSION_LIFETIME', 120),
'expire_on_close' => false,
'encrypt' => true,
'secure' => env('SESSION_SECURE_COOKIE', true),
'http_only' => true,
'same_site' => 'lax',
```

#### Multi-Factor Authentication
```php
// Using Laravel Fortify
'features' => [
    Features::twoFactorAuthentication([
        'confirm' => true,
        'confirmPassword' => true,
    ]),
],
```

### 8. Software and Data Integrity (A08:2021)

#### CSRF Protection
```php
// Automatic in web middleware
// In Blade forms:
<form method="POST">
    @csrf
    <!-- form fields -->
</form>

// For API - use Sanctum tokens instead of CSRF
// Exclude specific routes in VerifyCsrfToken middleware
protected $except = [
    'stripe/webhook',
    'external/callback',
];
```

#### Signed URLs
```php
// Generate signed URL
$url = URL::signedRoute('unsubscribe', ['user' => $user->id]);

// With expiration
$url = URL::temporarySignedRoute(
    'download',
    now()->addMinutes(30),
    ['file' => $fileId]
);

// Validate signed route
Route::get('/unsubscribe/{user}', function (Request $request) {
    if (!$request->hasValidSignature()) {
        abort(401);
    }
    // Process
})->name('unsubscribe')->middleware('signed');
```

### 9. Security Logging (A09:2021)

#### Audit Logging
```php
<?php

namespace App\Listeners;

use App\Events\DomainAction;
use Illuminate\Support\Facades\Log;

class LogDomainAction
{
    public function handle(DomainAction $event): void
    {
        Log::channel('audit')->info('Domain action', [
            'action' => $event->action,
            'entity' => $event->entity,
            'entity_id' => $event->entityId,
            'user_id' => $event->userId,
            'ip' => $event->ip,
            'user_agent' => $event->userAgent,
            'changes' => $event->changes,
            'timestamp' => now()->toISOString(),
        ]);
    }
}
```

#### Failed Login Monitoring
```php
// In EventServiceProvider
protected $listen = [
    \Illuminate\Auth\Events\Failed::class => [
        \App\Listeners\LogFailedLogin::class,
    ],
];

// LogFailedLogin listener
public function handle(Failed $event): void
{
    Log::channel('security')->warning('Failed login attempt', [
        'email' => $event->credentials['email'] ?? 'unknown',
        'ip' => request()->ip(),
        'user_agent' => request()->userAgent(),
    ]);
}
```

### 10. Server-Side Request Forgery (A10:2021)

#### URL Validation
```php
// Validate URLs before fetching
$rules = [
    'url' => ['required', 'url', 'active_url'],
    'callback_url' => [
        'required',
        'url',
        function ($attribute, $value, $fail) {
            $host = parse_url($value, PHP_URL_HOST);
            $allowedHosts = ['api.example.com', 'webhook.example.com'];

            if (!in_array($host, $allowedHosts)) {
                $fail('The callback URL host is not allowed.');
            }
        },
    ],
];
```

#### Block Internal IPs
```php
function isInternalIp(string $ip): bool
{
    return filter_var(
        $ip,
        FILTER_VALIDATE_IP,
        FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE
    ) === false;
}

// Before making external requests
$host = parse_url($url, PHP_URL_HOST);
$ip = gethostbyname($host);

if (isInternalIp($ip)) {
    throw new SecurityException('Access to internal resources is forbidden');
}
```

## File Upload Security

### Validation Rules
```php
public function rules(): array
{
    return [
        'document' => [
            'required',
            'file',
            'mimes:pdf,doc,docx',
            'max:10240', // 10MB
        ],
        'image' => [
            'required',
            'image',
            'mimes:jpg,jpeg,png,webp',
            'max:2048',
            'dimensions:min_width=100,min_height=100,max_width=4000,max_height=4000',
        ],
    ];
}
```

### Secure Storage
```php
public function store(StoreDocumentRequest $request): JsonResource
{
    $file = $request->file('document');

    // Generate random filename
    $filename = Str::uuid() . '.' . $file->getClientOriginalExtension();

    // Store outside public directory
    $path = $file->storeAs('documents', $filename, 'private');

    // Serve via signed URL
    return new DocumentResource([
        'url' => URL::temporarySignedRoute('document.download', now()->addHours(1), ['path' => $path]),
    ]);
}
```

### Content Type Validation
```php
// Don't trust client-provided MIME type
$mimeType = $file->getMimeType(); // Server-detected

$allowedMimes = ['image/jpeg', 'image/png', 'image/webp'];

if (!in_array($mimeType, $allowedMimes)) {
    throw new ValidationException('Invalid file type');
}
```

## API Security

### Input Sanitization
```php
// In Form Request
public function prepareForValidation(): void
{
    $this->merge([
        'email' => Str::lower(trim($this->email)),
        'name' => strip_tags($this->name),
    ]);
}
```

### Output Encoding
```php
// API Resources automatically encode
// For raw output, use htmlspecialchars
$safe = htmlspecialchars($userInput, ENT_QUOTES, 'UTF-8');
```

### CORS Configuration
```php
// config/cors.php
'paths' => ['api/*'],
'allowed_methods' => ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'],
'allowed_origins' => [env('FRONTEND_URL', 'http://localhost:3000')],
'allowed_origins_patterns' => [],
'allowed_headers' => ['Content-Type', 'Authorization', 'X-Requested-With'],
'exposed_headers' => [],
'max_age' => 0,
'supports_credentials' => true,
```

## Security Checklist

### Before Deployment
- [ ] `APP_DEBUG=false` in production
- [ ] `APP_ENV=production` in production
- [ ] Strong `APP_KEY` generated
- [ ] HTTPS enforced (`APP_URL` uses https)
- [ ] Database credentials secured
- [ ] Third-party API keys secured
- [ ] File permissions configured (755 directories, 644 files)
- [ ] Storage/cache directories not web-accessible
- [ ] Error pages don't expose sensitive info
- [ ] Rate limiting configured
- [ ] CORS configured correctly
- [ ] Session cookies are secure and HTTP-only
- [ ] CSRF protection enabled
- [ ] Input validation on all endpoints
- [ ] Output encoding for user-generated content
- [ ] SQL injection prevention verified
- [ ] File upload validation in place
- [ ] Audit logging enabled
- [ ] Dependencies up to date
- [ ] No debug routes exposed
- [ ] Admin routes protected

### Ongoing Maintenance
- [ ] Regular `composer audit` checks
- [ ] Monitor security advisories
- [ ] Review access logs
- [ ] Update dependencies monthly
- [ ] Penetration testing quarterly
- [ ] Security training for team
