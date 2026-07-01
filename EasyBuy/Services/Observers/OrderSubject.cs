using EasyBuy.Models;

namespace EasyBuy.Services.Observers
{
    public class OrderSubject
    {
        private readonly List<IOrderObserver> _observers = new();

        public void Attach(IOrderObserver observer) => _observers.Add(observer);

        public async Task NotifyAsync(Order order)
        {
            foreach (var observer in _observers)
            {
                await observer.UpdateAsync(order);
            }
        }
    }
}