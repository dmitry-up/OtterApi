using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OtterApi.Tests.Helpers;

// ── Domain models ──────────────────────────────────────────────────────────────

public class TestProduct
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int CategoryId { get; set; }

    // Navigation property (not a simple value type → treated as nav prop by OtterApi)
    public TestCategory? Category { get; set; }
}

public class TestCategory
{
    [Key] public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<TestProduct>? Products { get; set; }
}

/// <summary>Entity without a [Key] attribute – used to verify keyless-entity guards.</summary>
public class KeylessEntity
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>Entity used for per-entity query-filter integration tests.</summary>
public class TestItem
{
    [Key] public int Id       { get; set; }
    public string Name        { get; set; } = string.Empty;
    public bool IsActive      { get; set; }
    public int TenantId       { get; set; }
}

/// <summary>Entity used for soft-delete integration tests.</summary>
public class TestSoftDeleteItem
{
    [Key] public int Id    { get; set; }
    public string Name     { get; set; } = string.Empty;
    public bool IsDeleted  { get; set; }
}

// ── DbContext ──────────────────────────────────────────────────────────────────

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<TestProduct>         Products         { get; set; }
    public DbSet<TestCategory>        Categories       { get; set; }
    public DbSet<TestItem>            Items            { get; set; }
    public DbSet<TestSoftDeleteItem>  SoftDeleteItems  { get; set; }
}

public class KeylessDbContext : DbContext
{
    public KeylessDbContext(DbContextOptions<KeylessDbContext> options) : base(options) { }

    public DbSet<KeylessEntity> KeylessEntities { get; set; }
}

// ── Factory helpers ────────────────────────────────────────────────────────────

public static class DbContextFactory
{
    public static TestDbContext CreateInMemory(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    public static KeylessDbContext CreateKeylessInMemory()
    {
        var options = new DbContextOptionsBuilder<KeylessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KeylessDbContext(options);
    }
}

