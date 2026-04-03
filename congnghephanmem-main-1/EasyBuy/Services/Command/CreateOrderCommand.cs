using EasyBuy.Models;

namespace EasyBuy.Services.Command
{
    public class CreateOrderCommand : IOrderCommand
    {
        private readonly EasyBuyContext _context;

        public CreateOrderCommand(EasyBuyContext context)
        {
            _context = context;
        }

        public async Task<Order> ExecuteAsync(Order order, Cart cart)
        {
            _context.Orders.Add(order);
            cart.IsCheckedOut = true;

            foreach (var item in cart.CartItems)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product != null)
                {
                    product.Quantity -= item.Quantity ?? 0;
                }
            }

            await _context.SaveChangesAsync();
            return order;
        }
    }
}