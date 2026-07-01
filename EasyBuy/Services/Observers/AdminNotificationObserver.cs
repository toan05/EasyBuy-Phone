using EasyBuy.Models;
using EasyBuy.Services.Observers;

public class AdminNotificationObserver : IOrderObserver
{
    public Task UpdateAsync(Order order)
    {
        Console.WriteLine($"[Observer] Thông báo cho Admin: Có đơn hàng mới #{order.OrderId} giá trị {order.FinalTotal}đ");
        return Task.CompletedTask;
    }
}