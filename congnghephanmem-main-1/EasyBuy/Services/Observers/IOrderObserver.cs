using EasyBuy.Models;

namespace EasyBuy.Services.Observers
{
    public interface IOrderObserver
    {
        Task UpdateAsync(Order order);
    }
}