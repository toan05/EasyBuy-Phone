using EasyBuy.Models;
using EasyBuy.Services.EMAILOTP;

namespace EasyBuy.Services.Observers
{
    public class EmailNotificationObserver : IOrderObserver
    {
        private readonly IEmailService _emailService;
        // Chúng ta sẽ dùng logic gửi email bạn đã viết sẵn
        public EmailNotificationObserver(IEmailService emailService) => _emailService = emailService;

        public async Task UpdateAsync(Order order)
        {
            // Ở đây bạn gọi logic gửi email xác nhận mà bạn đã viết trong Controller
            // Mình sẽ giả lập gọi một hàm gửi mail
            Console.WriteLine($"[Observer] Đang gửi Email xác nhận cho đơn hàng #{order.OrderId}");
            // await _emailService.SendEmailAsync(...); 
        }
    }
}