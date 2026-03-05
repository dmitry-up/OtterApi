# OtterApi

![OtterApi](https://raw.githubusercontent.com/dmitry-up/OtterApi/refs/heads/master/icon.png)

[![NuGet](https://img.shields.io/nuget/v/OtterApi.svg)](https://www.nuget.org/packages/OtterApi)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/)


**OtterApi** is an ASP.NET Core library that automatically generates a full REST API on top of your EF Core models. Register your entities once — and GET / POST / PUT / DELETE routes, filtering, sorting, pagination, authorization, and Swagger documentation are all available without writing a single controller or repository.

---

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
  - [AddOtterApi](#addOtterApi)
  - [UseOtterApi](#useOtterApi)
  - [JsonSerializerOptions](#jsonserializeroptions)
- [Entity Registration](#entity-registration)
- [Authorization](#authorization)
- [REST API — Endpoint Reference](#rest-api--endpoint-reference)
- [Query Parameters](#query-parameters)
  - [Filtering](#filtering)
  - [Filter Operators](#filter-operators)
  - [Sorting](#sorting)
  - [Pagination](#pagination)
  - [PagedResult](#pagedresult)
  - [Include (Navigation Properties)](#include-navigation-properties)
  - [Count](#count)
  - [Compound Filters (AND / OR)](#compound-filters-and--or)
- [BeforeSave / AfterSave Hooks](#beforesave--aftersave-hooks)
  - [Lambda Approach](#lambda-approach)
  - [Handler Approach (Interface)](#handler-approach-interface)
- [Error Handling — OtterApiException](#error-handling--OtterApiexception)
- [Keyless Entities](#keyless-entities)
- [Swagger](#swagger)
- [Full Integration Example](#full-integration-example)
- [Limitations and Caveats](#limitations-and-caveats)

---

## Installation

```bash
dotnet add package OtterApi
```

**Dependencies** (installed automatically):

| Package | Version |
|---|---|
| Microsoft.EntityFrameworkCore | 8.0.0 |
| System.Linq.Dynamic.Core | 1.3.12 |
| Swashbuckle.AspNetCore.SwaggerGen | 6.3.1 |

---

## Quick Start

```csharp
// Program.cs / Startup.cs

// 1. Register services
builder.Services.AddOtterApi<AppDbContext>(options =>
{
    options.Path = "/api";
    options.Entity<Product>("products");
});

// 2. Register middleware (before UseEndpoints / MapControllers)
app.UseOtterApi();
```

After startup, the following endpoints are available:

```
GET    /api/products
GET    /api/products/{id}
POST   /api/products
PUT    /api/products/{id}
DELETE /api/products/{id}
GET    /api/products/count
GET    /api/products/pagedresult   (if enabled)
```

---

## Configuration

### AddOtterApi

Two overloads are available:

```csharp
// Overload 1 — path only
services.AddOtterApi<AppDbContext>("/api");

// Overload 2 — full configuration
services.AddOtterApi<AppDbContext>(options =>
{
    options.Path = "/api";
    options.Entity<Product>("products");
    options.Entity<Category>("categories").Authorize();
    // ...
});
```

| Property | Type | Description |
|---|---|---|
| `Path` | `string` | Base prefix for all generated routes. Example: `/api/v1` |
| `JsonSerializerOptions` | `JsonSerializerOptions?` | Global serialization options. See below. |

### UseOtterApi

```csharp
app.UseOtterApi();
```

Registers the middleware that intercepts incoming HTTP requests, matches them against registered entities, and executes CRUD operations. **Must be placed after `UseAuthentication()` / `UseAuthorization()`** and before `UseEndpoints()` / `MapControllers()`.

### JsonSerializerOptions

Global serialization options apply to both incoming request bodies and outgoing responses.

```csharp
services.AddOtterApi<AppDbContext>(options =>
{
    options.Path = "/api";
    options.JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    options.Entity<Product>("products");
});
```

> **Note.** `PropertyNameCaseInsensitive = true` and the enum converter (`OtterApiCaseInsensitiveEnumConverterFactory`) are always added automatically, regardless of your custom settings.

---

## Entity Registration

```csharp
options.Entity<TEntity>(route)
    .Authorize(bool authorize = true)
    .WithEntityPolicy(string policy)
    .WithGetPolicy(string policy)
    .WithPostPolicy(string policy)
    .WithPutPolicy(string policy)
    .WithDeletePolicy(string policy)
    .ExposePagedResult(bool expose = true)
    .BeforeSave(...)
    .AfterSave(...);
```

Every method returns the same `OtterApiEntityBuilder<T>` instance, so calls can be chained fluently.

**Entity requirements:**

- Must be registered as `DbSet<T>` in your `DbContext`.
- The key property must be marked with `[Key]` (or the entity must be keyless — GET only).
- Filterable/sortable property types: primitives, `string`, `Guid`, `DateTime`, `DateTimeOffset`, `enum`, and nullable variants of all the above.
- Navigation properties (collections, nested objects) are automatically excluded from filtering but are available via `?include=`.

---

## Authorization

OtterApi uses the standard ASP.NET Core `IAuthorizationService`, so all policies are configured the usual way.

```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("IsAdmin",   p => p.RequireRole("Admin"));
    options.AddPolicy("IsManager", p => p.RequireRole("Admin", "Manager"));
});
```

| Method | Description |
|---|---|
| `.Authorize()` | Requires authentication for all HTTP methods |
| `.WithEntityPolicy("IsAdmin")` | Applies a policy to all methods (GET/POST/PUT/DELETE) |
| `.WithGetPolicy("IsManager")` | Policy for GET only |
| `.WithPostPolicy("IsAdmin")` | Policy for POST only |
| `.WithPutPolicy("IsAdmin")` | Policy for PUT only |
| `.WithDeletePolicy("IsAdmin")` | Policy for DELETE only |

Policies can be combined: `EntityPolicy` is checked first, then the method-specific policy.

```csharp
options.Entity<Product>("products")
    .Authorize()                        // any authenticated user
    .WithEntityPolicy("IsManager")      // ... who also has the Manager role
    .WithDeletePolicy("IsAdmin");       // DELETE requires Admin role
```

**Authorization error codes:**
- `401 Unauthorized` — user is not authenticated
- `403 Forbidden` — authenticated but lacks required permissions

---

## REST API — Endpoint Reference

The following example model is used throughout this section:

```csharp
public enum ProductStatus { Pending, Active, Discontinued }

public class Product
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; }

    public decimal Price { get; set; }

    public int Stock { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public ProductStatus Status { get; set; }

    public int CategoryId { get; set; }

    public Category? Category { get; set; }  // navigation property
}
```

Registered as:

```csharp
options.Path = "/api";
options.Entity<Product>("products").ExposePagedResult();
```

---

### GET /api/products

Returns all records, sorted by Id descending by default.

```http
GET /api/products
```

**Response `200 OK`:**
```json
[
  { "id": 3, "name": "Laptop",   "price": 999.99, "stock": 10, "isActive": true,  "status": "active",   "categoryId": 1 },
  { "id": 2, "name": "Mouse",    "price": 29.99,  "stock": 50, "isActive": true,  "status": "active",   "categoryId": 1 },
  { "id": 1, "name": "Old Desk", "price": 349.00, "stock": 5,  "isActive": false, "status": "pending",  "categoryId": 2 }
]
```

---

### GET /api/products/{id}

Returns a single record by primary key.

```http
GET /api/products/3
```

**Responses:**
- `200 OK` — record found
- `404 Not Found` — record does not exist

```json
{ "id": 3, "name": "Laptop", "price": 999.99, "stock": 10, "isActive": true, "categoryId": 1 }
```

---

### POST /api/products

Creates a new record. Request body is a JSON object of the entity.

```http
POST /api/products
Content-Type: application/json

{
  "name": "Keyboard",
  "price": 79.99,
  "stock": 25,
  "isActive": true,
  "status": "pending",
  "categoryId": 1
}
```

**Responses:**
- `201 Created` — includes `Location: /api/products/4` header and the created object in the body
- `400 Bad Request` — model validation failed (e.g. a `[Required]` field is missing)

---

### PUT /api/products/{id}

Updates an existing record. The Id in the request body **must match** the Id in the URL.

```http
PUT /api/products/4
Content-Type: application/json

{
  "id": 4,
  "name": "Mechanical Keyboard",
  "price": 129.99,
  "stock": 20,
  "isActive": true,
  "status": "active",
  "categoryId": 1
}
```

**Responses:**
- `200 OK` — updated object
- `400 Bad Request` — Id mismatch between URL and body, or validation errors
- `404 Not Found` — record does not exist

---

### DELETE /api/products/{id}

Deletes a record by Id.

```http
DELETE /api/products/4
```

**Responses:**
- `200 OK`
- `404 Not Found`

---

### GET /api/products/count

Returns the total number of records, respecting any filters passed as query parameters.

```http
GET /api/products/count
GET /api/products/count?filter[isActive]=true
```

**Response `200 OK`:**
```json
42
```

---

### GET /api/products/pagedresult

Returns results in a paginated envelope. Only available when `.ExposePagedResult()` is called during entity registration.

```http
GET /api/products/pagedresult?page=2&pagesize=10
```

**Response `200 OK`:**
```json
{
  "items": [ ... ],
  "page": 2,
  "pageSize": 10,
  "pageCount": 5,
  "total": 47
}
```

---

## Query Parameters

### Filtering

Syntax: `filter[propertyName]=value` (default operator is `eq`)

```http
GET /api/products?filter[isActive]=true
GET /api/products?filter[name]=Laptop
GET /api/products?filter[categoryId]=1
```

### Filter Operators

Syntax: `filter[propertyName][operator]=value`

| Operator | Supported types | Description | Example |
|---|---|---|---|
| `eq` | string, value types, Guid | Equal to | `filter[name][eq]=Laptop` |
| `neq` | string, value types, Guid | Not equal to | `filter[status][neq]=pending` |
| `like` | string | Contains substring | `filter[name][like]=key` |
| `nlike` | string | Does not contain substring | `filter[name][nlike]=old` |
| `lt` | value types (not Guid) | Less than | `filter[price][lt]=100` |
| `lteq` | value types (not Guid) | Less than or equal to | `filter[price][lteq]=100` |
| `gt` | value types (not Guid) | Greater than | `filter[stock][gt]=0` |
| `gteq` | value types (not Guid) | Greater than or equal to | `filter[price][gteq]=50` |
| `in` | string, value types, Guid | Value is in a JSON array | `filter[categoryId][in]=[1,2,3]` |
| `nin` | string, value types, Guid | Value is not in a JSON array | `filter[status][nin]=["pending","discontinued"]` |

```http
GET /api/products?filter[price][gteq]=50&filter[price][lteq]=500
GET /api/products?filter[name][like]=book
GET /api/products?filter[categoryId][in]=[1,2,5]
GET /api/products?filter[status][nin]=["pending","discontinued"]
```

### Sorting

Syntax: `sort[propertyName]=asc|desc`

Descending values: `desc`, `1`, `descending`. Everything else is treated as ascending.

```http
GET /api/products?sort[price]=asc
GET /api/products?sort[name]=desc
GET /api/products?sort[price]=asc&sort[name]=desc
```

> If no sort is specified, results are ordered by the `[Key]` property descending.

### Pagination

| Parameter | Description |
|---|---|
| `page` | Page number, starting from 1 (default: 1) |
| `pagesize` | Number of items per page |

```http
GET /api/products?page=1&pagesize=20
GET /api/products?filter[isActive]=true&sort[name]=asc&page=2&pagesize=10
```

> Using `page` / `pagesize` on a regular GET request returns a flat array with `Skip` / `Take` applied. For a JSON envelope with metadata, use `/pagedresult`.

### PagedResult

Available only when `.ExposePagedResult()` is configured for the entity.

```http
GET /api/products/pagedresult
GET /api/products/pagedresult?page=3&pagesize=5&filter[isActive]=true&sort[price]=asc
```

**Response structure:**
```json
{
  "items": [ ... ],
  "page": 3,
  "pageSize": 5,
  "pageCount": 10,
  "total": 49
}
```

| Field | Description |
|---|---|
| `items` | Array of objects for the current page |
| `page` | Current page number |
| `pageSize` | Items per page |
| `pageCount` | Total number of pages |
| `total` | Total number of records matching the query |

### Include (Navigation Properties)

Eagerly loads related entities, equivalent to EF Core's `Include()`.

Syntax: `include=NavPropertyName1,NavPropertyName2`

```http
GET /api/products?include=Category
GET /api/products?filter[isActive]=true&include=Category
```

> Only navigation properties declared directly on the entity are supported. Unknown or scalar properties in `include` are silently ignored. Nested includes (deeper than one level) are not supported.

### Count

Count can be combined with any filters:

```http
GET /api/products/count?filter[isActive]=true
GET /api/products/count?filter[price][gt]=100&filter[stock][gt]=0
```

**Response:** a plain integer with `200 OK`.

### Compound Filters (AND / OR)

By default, multiple `filter[...]` parameters are joined with `AND`.

To use `OR`, add the `operator=or` parameter:

```http
GET /api/products?filter[name][like]=laptop&filter[name][like]=keyboard&operator=or
```

| `operator` value | Logic |
|---|---|
| *(not specified)* | AND |
| `or` | OR |

> **Note.** The `operator` parameter is global for the entire request — it is not possible to mix AND and OR conditions for different fields in a single query.

---

## BeforeSave / AfterSave Hooks

Hooks let you execute arbitrary logic **before** or **after** an entity is saved to the database.

### Signature

```
(DbContext context, T newEntity, T? originalEntity, OtterApiCrudOperation operation)
```

| Parameter | Description |
|---|---|
| `context` | The current DbContext instance |
| `newEntity` | Incoming data for `BeforeSave`; saved data for `AfterSave` |
| `originalEntity` | The database state before the change. `null` for `POST`. For `DELETE`, equals `newEntity`. |
| `operation` | `OtterApiCrudOperation.Post`, `.Put`, or `.Delete` |

### `OtterApiCrudOperation`

```csharp
public enum OtterApiCrudOperation
{
    Post,
    Put,
    Delete
}
```

---

### Lambda Approach

Best for concise inline logic. Both synchronous (`Action`) and asynchronous (`Func<..., Task>`) overloads are supported.

#### BeforeSave

```csharp
// Synchronous
options.Entity<Product>("products")
    .BeforeSave((DbContext ctx, Product newProduct, Product? original, OtterApiCrudOperation op) =>
    {
        if (op == OtterApiCrudOperation.Post)
            newProduct.CreatedAt = DateTime.UtcNow;

        if (op == OtterApiCrudOperation.Put && original != null)
        {
            if (original.Price != newProduct.Price)
                Console.WriteLine($"Price changed: {original.Price} → {newProduct.Price}");
        }
    });

// Asynchronous
options.Entity<Product>("products")
    .BeforeSave(async (DbContext ctx, Product newProduct, Product? original, OtterApiCrudOperation op) =>
    {
        if (op == OtterApiCrudOperation.Post)
        {
            var exists = await ctx.Set<Product>()
                .AnyAsync(p => p.Name == newProduct.Name);
            if (exists)
                throw new OtterApiException("DUPLICATE_NAME", "A product with this name already exists.", 409);
        }
    });
```

#### AfterSave

```csharp
// Synchronous
options.Entity<Product>("products")
    .AfterSave((DbContext ctx, Product saved, Product? original, OtterApiCrudOperation op) =>
    {
        if (op == OtterApiCrudOperation.Post)
            Console.WriteLine($"New product created: id={saved.Id}");
    });

// Asynchronous
options.Entity<Product>("products")
    .AfterSave(async (DbContext ctx, Product saved, Product? original, OtterApiCrudOperation op) =>
    {
        if (op == OtterApiCrudOperation.Delete)
            await NotificationService.SendAsync($"Product '{saved.Name}' was deleted.");
    });
```

> `BeforeSave` is called **before** `dbContext.SaveChangesAsync()`.  
> `AfterSave` is called **after** `dbContext.SaveChangesAsync()`.

---

### Handler Approach (Interface)

Best for complex logic that uses DI dependencies, or when you want to separate concerns into dedicated classes.

#### IOtterApiBeforeSaveHandler\<T\>

```csharp
public interface IOtterApiBeforeSaveHandler<T> where T : class
{
    Task BeforeSaveAsync(DbContext context, T newEntity, T? originalEntity, OtterApiCrudOperation operation);
}
```

#### IOtterApiAfterSaveHandler\<T\>

```csharp
public interface IOtterApiAfterSaveHandler<T> where T : class
{
    Task AfterSaveAsync(DbContext context, T newEntity, T? originalEntity, OtterApiCrudOperation operation);
}
```

#### Implementation Example

```csharp
// ProductBeforeSaveHandler.cs
public class ProductBeforeSaveHandler : IOtterApiBeforeSaveHandler<Product>
{
    private readonly ILogger<ProductBeforeSaveHandler> _logger;

    public ProductBeforeSaveHandler(ILogger<ProductBeforeSaveHandler> logger)
    {
        _logger = logger;
    }

    public async Task BeforeSaveAsync(
        DbContext context,
        Product newProduct,
        Product? original,
        OtterApiCrudOperation operation)
    {
        if (operation == OtterApiCrudOperation.Post)
        {
            newProduct.CreatedAt = DateTime.UtcNow;
            _logger.LogInformation("Creating product: {Name}", newProduct.Name);

            var duplicate = await context.Set<Product>()
                .AnyAsync(p => p.Name == newProduct.Name);

            if (duplicate)
                throw new OtterApiException("DUPLICATE", "A product with this name already exists.", 409);
        }

        if (operation == OtterApiCrudOperation.Put && original != null)
        {
            _logger.LogInformation(
                "Updating product {Id}: price {Old} → {New}",
                newProduct.Id, original.Price, newProduct.Price);
        }
    }
}

// ProductAfterSaveHandler.cs
public class ProductAfterSaveHandler : IOtterApiAfterSaveHandler<Product>
{
    private readonly IEventBus _eventBus;

    public ProductAfterSaveHandler(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public async Task AfterSaveAsync(
        DbContext context,
        Product saved,
        Product? original,
        OtterApiCrudOperation operation)
    {
        await _eventBus.PublishAsync(new ProductChangedEvent
        {
            ProductId = saved.Id,
            Operation = operation.ToString()
        });
    }
}
```

#### Registering Handlers

Pass a handler instance directly to the builder method. If your handler requires DI dependencies, resolve or construct it before calling `AddOtterApi`:

```csharp
// Option 1 — construct the handler manually
var beforeHandler = new ProductBeforeSaveHandler(logger);
var afterHandler  = new ProductAfterSaveHandler(eventBus);

services.AddOtterApi<AppDbContext>(options =>
{
    options.Path = "/api";
    options.Entity<Product>("products")
        .BeforeSave(beforeHandler)
        .AfterSave(afterHandler);
});

// Option 2 — mix handlers and lambdas across different entities
services.AddOtterApi<AppDbContext>(options =>
{
    options.Path = "/api";

    options.Entity<Product>("products")
        .BeforeSave(new ProductBeforeSaveHandler(loggerFactory.CreateLogger<ProductBeforeSaveHandler>()))
        .AfterSave(new ProductAfterSaveHandler(eventBus));

    options.Entity<Category>("categories")
        .BeforeSave((ctx, cat, _, op) =>
        {
            if (op == OtterApiCrudOperation.Post)
                cat.Name = cat.Name.Trim();
        });
});
```

> **Important.** Hooks are registered once at application startup. If your handler requires scoped dependencies (e.g. another DbContext, HTTP client, etc.), use the lambda approach and resolve dependencies through the provided `DbContext` or a scope factory.

---

## Error Handling — OtterApiException

To return a structured error response from a `BeforeSave` or `AfterSave` hook, throw `OtterApiException`:

```csharp
throw new OtterApiException(
    code: "OUT_OF_STOCK",
    message: "Cannot create order: the product is out of stock.",
    statusCode: 422
);
```

The middleware catches the exception and returns:

```
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json

{
  "code": "OUT_OF_STOCK",
  "message": "Cannot create order: the product is out of stock."
}
```

| Parameter | Type | Description |
|---|---|---|
| `code` | `string` | Machine-readable error code |
| `message` | `string` | Human-readable error message |
| `statusCode` | `int` | HTTP status code (default: `400`) |

---

## Keyless Entities

If an entity is marked `[Keyless]` (e.g. a database view), it is registered in OtterApi as **read-only**: only `GET` is available.

```csharp
[Keyless]
public class ProductSummaryView
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string CategoryName { get; set; }
}
```

```csharp
options.Entity<ProductSummaryView>("product-summary");
```

Attempting POST, PUT, or DELETE on a keyless entity will result in an exception: `"Operation not allowed for keyless entities"`.

---

## Swagger

OtterApi ships with `OtterApiSwaggerDocumentFilter`, which adds all auto-generated routes to the Swagger documentation.

```csharp
services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
    options.DocumentFilter<OtterApiSwaggerDocumentFilter>();  // <-- add this
});
```

The filter automatically generates:
- Schemas for all registered entities
- GET (list, by id, count, pagedresult), POST, PUT, and DELETE operations
- Query parameter descriptions (filters, sorting, pagination, include)
- Type mappings for: `string`, `int`, `long`, `float`, `double`, `decimal`, `bool`, `DateTime`, `DateTimeOffset`, `Guid`, `byte`, `enum`

---

## Full Integration Example

### Models

```csharp
public enum ProductStatus { Pending, Active, Discontinued }

public class Category
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; }

    public List<Product> Products { get; set; }
}

public class Product
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; }

    public decimal Price { get; set; }

    public int Stock { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public ProductStatus Status { get; set; }

    public int CategoryId { get; set; }

    public Category? Category { get; set; }
}
```

### DbContext

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products { get; set; }
    public DbSet<Category> Categories { get; set; }
}
```

### Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite("Data Source=app.db"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(/* ... */);

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("IsAdmin",   p => p.RequireRole("Admin"));
    opt.AddPolicy("IsManager", p => p.RequireRole("Admin", "Manager"));
});

builder.Services.AddControllers();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new OpenApiInfo { Title = "Shop API", Version = "v1" });
    opt.DocumentFilter<OtterApiSwaggerDocumentFilter>();
});

// --- OtterApi ---
builder.Services.AddOtterApi<AppDbContext>(options =>
{
    options.Path = "/api/v1";

    options.Entity<Category>("categories")
        .Authorize()
        .WithDeletePolicy("IsAdmin")
        .BeforeSave((ctx, cat, _, op) =>
        {
            if (op == OtterApiCrudOperation.Post)
                cat.Name = cat.Name.Trim();
        });

    options.Entity<Product>("products")
        .Authorize()
        .WithPostPolicy("IsManager")
        .WithPutPolicy("IsManager")
        .WithDeletePolicy("IsAdmin")
        .ExposePagedResult()
        .BeforeSave(async (ctx, product, original, op) =>
        {
            if (op == OtterApiCrudOperation.Post)
            {
                product.CreatedAt = DateTime.UtcNow;

                var categoryExists = await ctx.Set<Category>().AnyAsync(c => c.Id == product.CategoryId);
                if (!categoryExists)
                    throw new OtterApiException("INVALID_CATEGORY", "The specified category was not found.", 404);
            }

            if (op == OtterApiCrudOperation.Put && original != null && original.Price != product.Price)
            {
                // log price change
            }
        })
        .AfterSave(async (ctx, product, _, op) =>
        {
            if (op == OtterApiCrudOperation.Post)
            {
                // e.g. send a notification
                await Task.CompletedTask;
            }
        });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.UseOtterApi();          // OtterApi middleware — must come after auth
app.MapControllers();

app.Run();
```

### Sample Requests

```http
# All categories
GET /api/v1/categories

# Active products with price >= 100, sorted by name, page 2
GET /api/v1/products?filter[isActive]=true&filter[price][gteq]=100&sort[name]=asc&page=2&pagesize=15

# Products with status Active or Pending
GET /api/v1/products?filter[status][in]=["active","pending"]

# Products with category eagerly loaded
GET /api/v1/products?include=Category

# Paginated result envelope
GET /api/v1/products/pagedresult?page=1&pagesize=20&sort[price]=desc

# Count active products
GET /api/v1/products/count?filter[isActive]=true

# Create a product (requires Manager role)
POST /api/v1/products
Authorization: Bearer <token>
Content-Type: application/json
{ "name": "Widget", "price": 9.99, "stock": 100, "isActive": true, "status": "pending", "categoryId": 1 }

# Update a product
PUT /api/v1/products/7
Authorization: Bearer <token>
Content-Type: application/json
{ "id": 7, "name": "Widget Pro", "price": 14.99, "stock": 80, "isActive": true, "status": "active", "categoryId": 1 }

# Delete a product (requires Admin role)
DELETE /api/v1/products/7
Authorization: Bearer <token>
```

---

## Limitations and Caveats

| Limitation | Details |
|---|---|
| **Single `[Key]`** | Composite primary keys are not supported. |
| **EF Core `DbSet`** | The entity must be registered as `DbSet<T>` in the provided `DbContext`. If it is not, an `InvalidOperationException` is thrown at startup. |
| **Filterable property types** | Supported: primitives, `string`, `Guid`, `DateTime`, `DateTimeOffset`, `enum`, and nullable variants. Objects and collections cannot be used as filter fields. |
| **`include` depth** | Only navigation properties declared directly on the entity are loaded. Nested includes (deeper than one level) are not supported. Unknown property names in `include` are silently ignored. |
| **One hook per entity** | Only one `BeforeSave` and one `AfterSave` can be registered per entity. Calling `.BeforeSave()` a second time overwrites the first. |
| **Hooks and DI** | Hooks are registered once at startup. Scoped dependencies must be resolved manually through the provided `DbContext` or a service scope factory. |
| **Keyless entities** | `GET` only (list + filter). POST, PUT, and DELETE throw an exception. |
| **`operator=or` is global** | The `operator=or` parameter switches the join logic for **all** filters in the request. Mixing AND and OR for different fields in a single query is not supported. |
| **Validation** | OtterApi validates Data Annotations (`[Required]`, `[MaxLength]`, etc.) using the standard `IObjectModelValidator`. Invalid requests return `400 Bad Request` with the model state. |
| **Enum deserialization** | Enums are deserialized case-insensitively as both strings and numbers. |
| **Middleware order** | `UseOtterApi()` must be placed **after** `UseAuthentication()` / `UseAuthorization()` and **before** `UseEndpoints()` / `MapControllers()`. |
| **Target framework** | .NET 8.0 is required. |
