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

    options.Entity<Category>("categories")
        .ExposePagedResult();

    options.Entity<Product>("products")
        .ExposePagedResult();

    options.Entity<Order>("orders")
        .Allow(OtterApiCrudOperation.Get | OtterApiCrudOperation.Post)
        .ExposePagedResult()
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