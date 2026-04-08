using EasyBuy.Models;
using System.Threading.Tasks;

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
                if (item.Product != null && item.Quantity.HasValue)
                {
                    item.Product.Quantity -= item.Quantity.Value;
                }
            }

            return order;
        }
    }
}