using EasyBuy.Models;

namespace EasyBuy.Services.Command
{
    public interface IOrderCommand
    {
        Task<Order> ExecuteAsync(Order order, Cart cart);
    }
}