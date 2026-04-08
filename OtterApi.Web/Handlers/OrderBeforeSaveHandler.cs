using Microsoft.EntityFrameworkCore;
using OtterApi.Enums;
using OtterApi.Interfaces;
using OtterApi.Web.Models;

namespace OtterApi.Web.Handlers;

public class OrderBeforeSaveHandler : IOtterApiBeforeSaveHandler<Order>
{
    public async Task BeforeSaveAsync(
        DbContext context,
        Order newOrder,
        Order? originalOrder,
        OtterApiCrudOperation operation)
    {
        if (operation == OtterApiCrudOperation.Post)
        {
            newOrder.CreatedAt = DateTime.UtcNow;
            newOrder.Status    = OrderStatus.Pending;
        }

        var product = await context.Set<Product>().FindAsync(newOrder.ProductId)
                      ?? throw new InvalidOperationException($"Product with Id={newOrder.ProductId} not found.");

        newOrder.TotalPrice = product.Price * newOrder.Quantity;

        Console.WriteLine(
            $"[BeforeSave] Order for '{newOrder.CustomerName}' — " +
            $"'{product.Name}' × {newOrder.Quantity} = {newOrder.TotalPrice:C}");
    }
}
