using Microsoft.EntityFrameworkCore;
using OtterApi.Configs;
using OtterApi.Enums;
using OtterApi.Filters;
using OtterApi.Web.Data;
using OtterApi.Web.Handlers;
using OtterApi.Web.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DemoDbContext>(opt =>
    opt.UseInMemoryDatabase("OtterApiDemo"));

builder.Services.AddOtterApi<DemoDbContext>(options =>
{
    options.Path = "/api";

    // Only expose active categories — archived ones are invisible to consumers.
    // GET /api/categories        → returns only IsActive == true
    // GET /api/categories/{id}   → 404 when the category is archived
    options.Entity<Category>("categories")
        .ExposePagedResult()
        .WithQueryFilter(c => c.IsActive);

    // Only expose products that are currently available.
    // The "Discontinued Gadget" (Id=10, IsAvailable=false) is hidden from all GET endpoints.
    // Two chained filters demonstrate AND semantics:
    //   filter 1 — must be available
    //   filter 2 — must have stock > 0  (belt-and-suspenders: IsAvailable is already set to false when Stock hits 0)
    options.Entity<Product>("products")
        .ExposePagedResult()
        .WithQueryFilter(p => p.IsAvailable)
        .WithQueryFilter(p => p.Stock > 0);

    // Hide cancelled orders from the public listing.
    // Single filter with a compound condition: not cancelled AND not pending.
    // Only active, confirmed, shipped, or delivered orders are visible.
    options.Entity<Order>("orders")
        .Allow(OtterApiCrudOperation.Get | OtterApiCrudOperation.Post)
        .ExposePagedResult()
        .WithQueryFilter(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Pending)
        .BeforeSave(new OrderBeforeSaveHandler())
        .AfterSave(new OrderAfterSaveHandler());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "OtterApi Demo", Version = "v1" });
    c.DocumentFilter<OtterApiSwaggerDocumentFilter>();
});

builder.Services.AddControllers();
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DemoDbContext>();
    DemoDataSeeder.Seed(db);
}

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OtterApi Demo v1"));

app.UseAuthorization();
app.UseOtterApi();

app.Run();