using OtterApi.Web.Models;

namespace OtterApi.Web.Data;

public static class DemoDataSeeder
{
    public static void Seed(DemoDbContext db)
    {
        // ── Categories ────────────────────────────────────────────
        var electronics = new Category { Id = 1, Name = "Electronics",   Description = "Gadgets and devices",          IsActive = true,  CreatedAt = DateTime.UtcNow.AddDays(-30) };
        var clothing    = new Category { Id = 2, Name = "Clothing",      Description = "Apparel and accessories",      IsActive = true,  CreatedAt = DateTime.UtcNow.AddDays(-20) };
        var food        = new Category { Id = 3, Name = "Food & Drinks", Description = "Groceries and beverages",      IsActive = true,  CreatedAt = DateTime.UtcNow.AddDays(-10) };
        var archived    = new Category { Id = 4, Name = "Archived",      Description = "Old, no longer active category", IsActive = false, CreatedAt = DateTime.UtcNow.AddDays(-90) };

        db.Categories.AddRange(electronics, clothing, food, archived);

        // ── Products ──────────────────────────────────────────────
        db.Products.AddRange(
            new Product { Id = 1,  Name = "Laptop Pro 15",       Price = 1299.99m, Stock = 50,  IsAvailable = true,  CategoryId = 1 },
            new Product { Id = 2,  Name = "Wireless Headphones",  Price = 199.99m,  Stock = 120, IsAvailable = true,  CategoryId = 1 },
            new Product { Id = 3,  Name = "Smart Watch",          Price = 349.50m,  Stock = 75,  IsAvailable = true,  CategoryId = 1 },
            new Product { Id = 4,  Name = "USB-C Hub",            Price = 49.99m,   Stock = 200, IsAvailable = true,  CategoryId = 1 },
            new Product { Id = 5,  Name = "Running Shoes",        Price = 89.99m,   Stock = 60,  IsAvailable = true,  CategoryId = 2 },
            new Product { Id = 6,  Name = "Winter Jacket",        Price = 159.00m,  Stock = 30,  IsAvailable = true,  CategoryId = 2 },
            new Product { Id = 7,  Name = "Classic T-Shirt",      Price = 24.99m,   Stock = 300, IsAvailable = true,  CategoryId = 2 },
            new Product { Id = 8,  Name = "Organic Coffee Beans", Price = 19.99m,   Stock = 500, IsAvailable = true,  CategoryId = 3 },
            new Product { Id = 9,  Name = "Sparkling Water 6-pack", Price = 5.49m,  Stock = 800, IsAvailable = true,  CategoryId = 3 },
            new Product { Id = 10, Name = "Discontinued Gadget",  Price = 9.99m,    Stock = 0,   IsAvailable = false, CategoryId = 4 }
        );

        // ── Orders ────────────────────────────────────────────────
        // Id=6 is soft-deleted — invisible via GET but remains in the database.
        // Try: GET /api/orders (returns 3 items), GET /api/orders/6 (404).
        db.Orders.AddRange(
            new Order { Id = 1, CustomerName = "Alice Johnson",  CustomerEmail = "alice@example.com",  ProductId = 1, Quantity = 1, TotalPrice = 1299.99m, Status = OrderStatus.Delivered, CreatedAt = DateTime.UtcNow.AddDays(-15) },
            new Order { Id = 2, CustomerName = "Bob Smith",      CustomerEmail = "bob@example.com",    ProductId = 2, Quantity = 2, TotalPrice = 399.98m,  Status = OrderStatus.Shipped,   CreatedAt = DateTime.UtcNow.AddDays(-7)  },
            new Order { Id = 3, CustomerName = "Carol White",    CustomerEmail = "carol@example.com",  ProductId = 5, Quantity = 1, TotalPrice = 89.99m,   Status = OrderStatus.Confirmed, CreatedAt = DateTime.UtcNow.AddDays(-3)  },
            new Order { Id = 4, CustomerName = "David Brown",    CustomerEmail = "david@example.com",  ProductId = 8, Quantity = 3, TotalPrice = 59.97m,   Status = OrderStatus.Pending,   CreatedAt = DateTime.UtcNow.AddDays(-1)  },
            new Order { Id = 5, CustomerName = "Eva Martinez",   CustomerEmail = "eva@example.com",    ProductId = 3, Quantity = 1, TotalPrice = 349.50m,  Status = OrderStatus.Cancelled, CreatedAt = DateTime.UtcNow.AddDays(-20) },
            new Order { Id = 6, CustomerName = "Frank Deleted",  CustomerEmail = "frank@example.com",  ProductId = 4, Quantity = 1, TotalPrice = 49.99m,   Status = OrderStatus.Confirmed, CreatedAt = DateTime.UtcNow.AddDays(-5), IsDeleted = true }
        );

        db.SaveChanges();
    }
}

