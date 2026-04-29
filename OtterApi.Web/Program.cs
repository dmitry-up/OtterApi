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

builder.Services.AddHttpContextAccessor();

builder.Services.AddOtterApi<DemoDbContext>(options =>
{
    options.Path = "/api";

    // Only expose active categories — archived ones are invisible to consumers.
    // Custom routes:
    //   GET /api/categories/recent — last 3 categories by creation date
    options.Entity<Category>("categories")
        .ExposePagedResult()
        .WithQueryFilter(c => c.IsActive)
        .WithCustomRoute("recent",
            sort: "CreatedAt desc",
            take: 3);

    // Only expose products that are currently available and in stock.
    // Custom routes:
    //   GET /api/products/featured — top-5 most expensive available products
    //   GET /api/products/cheap    — up to 10 cheapest products, price < 50
    options.Entity<Product>("products")
        .ExposePagedResult()
        .WithQueryFilter(p => p.IsAvailable)
        .WithQueryFilter(p => p.Stock > 0)
        .WithCustomRoute("featured",
            sort: "Price desc",
            take: 5)
        .WithCustomRoute("cheap",
            filter: p => p.Price < 50m,
            sort:   "Price asc",
            take:   10);

    // Soft-delete: DELETE sets IsDeleted=true instead of removing the row.
    // The auto-registered query filter (IsDeleted==false) hides soft-deleted orders
    // from all GET endpoints automatically — no extra WithQueryFilter needed.
    // Custom routes:
    //   GET /api/orders/latest — the most recently confirmed/shipped/delivered order
    options.Entity<Order>("orders")
        .Allow(OtterApiCrudOperation.Get | OtterApiCrudOperation.Post | OtterApiCrudOperation.Delete)
        .ExposePagedResult()
        .WithSoftDelete(o => o.IsDeleted)
        .WithQueryFilter(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Pending)
        // Scoped filter: каждый пользователь видит только свои заказы (по email из токена)
        // .WithScopedQueryFilter(sp =>
        // {
        //     var http = sp.GetRequiredService<IHttpContextAccessor>();
        //     var email = http.HttpContext?.User.FindFirst("email")?.Value ?? "";
        //     return o => o.CustomerEmail == email;
        // })
        .WithCustomRoute("latest",
            sort:   "CreatedAt desc",
            take:   1,
            single: true)
        // Цепочка: оба хендлера вызываются по порядку
        .BeforeSave(new OrderBeforeSaveHandler())
        .BeforeSave((_, order, _, op) =>
        {
            if (op == OtterApiCrudOperation.Post)
                Console.WriteLine($"[Audit] New order: {order.CustomerName} → {order.TotalPrice:C}");
        })
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