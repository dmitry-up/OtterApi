using Microsoft.EntityFrameworkCore;
using OtterApi.Enums;
using OtterApi.Interfaces;
using OtterApi.Web.Models;

namespace OtterApi.Web.Handlers;

public class OrderAfterSaveHandler : IOtterApiAfterSaveHandler<Order>
{
    public async Task AfterSaveAsync(
        DbContext context,
        Order savedOrder,
        Order? originalOrder,
        OtterApiCrudOperation operation)
    {
        if (operation == OtterApiCrudOperation.Post)
        {
            var product = await context.Set<Product>().FindAsync(savedOrder.ProductId);
            if (product != null)
            {
                product.Stock = Math.Max(0, product.Stock - savedOrder.Quantity);
                product.IsAvailable = product.Stock > 0;
                await context.SaveChangesAsync();

                Console.WriteLine(
                    $"[AfterSave] Order #{savedOrder.Id} saved. " +
                    $"Stock for '{product.Name}' → {product.Stock}.");
            }
        }
    }
}
