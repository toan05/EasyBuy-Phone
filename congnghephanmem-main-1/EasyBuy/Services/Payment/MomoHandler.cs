using EasyBuy.Models;
using EasyBuy.Models.MOMO;
using EasyBuy.Models.VNPAY;
using EasyBuy.Services.MOMO;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace EasyBuy.Services.Payment
{
    public class MomoHandler : IPaymentHandler
    {
        private readonly IMomoService _service;
        private readonly IServiceProvider _serviceProvider;

        public MomoHandler(IMomoService service, IServiceProvider serviceProvider)
        {
            _service = service;
            _serviceProvider = serviceProvider;
        }

        public async Task<string> HandleAsync(PaymentInformationModel model, HttpContext context)
        {
            using var scope = _serviceProvider.CreateScope();
            var _context = scope.ServiceProvider.GetRequiredService<EasyBuyContext>();

            var userId = context.Session.GetInt32("UserID");
            var cartId = context.Session.GetInt32("CheckoutCartId");
            var addressId = context.Session.GetInt32("CheckoutAddressId");
            var paymentMethodId = context.Session.GetInt32("CheckoutPaymentMethodId");
            var voucherId = context.Session.GetInt32("CheckoutVoucherId");
            var totalStr = context.Session.GetString("CheckoutTotalAmount");
            var discountStr = context.Session.GetString("CheckoutDiscount");
            var finalAmountStr = context.Session.GetString("CheckoutFinnalAmout");

            if (userId == null || cartId == null || addressId == null || paymentMethodId == null)
                return "/Order/Checkout";

            decimal.TryParse(totalStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal total);
            decimal.TryParse(discountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal discount);
            decimal.TryParse(finalAmountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal finalAmount);

            // Create order with pending payment status
            var cart = await _context.Carts
                .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                .FirstOrDefaultAsync(c => c.CartId == cartId && c.IsCheckedOut == false);

            if (cart == null)
                return "/Order/Checkout";

            var productIds = cart.CartItems.Select(c => c.ProductId).ToList();
            var products = _context.Products
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionary(p => p.ProductId, p => p.Quantity.Value);

            var order = new Order
            {
                UserId = userId.Value,
                AddressId = addressId.Value,
                PaymentMethodId = paymentMethodId.Value,
                VoucherId = voucherId == 0 ? null : voucherId,
                TotalAmount = total,
                FinalTotal = finalAmount,
                Status = "Chờ thanh toán",
                StatusPayment = "Chờ thanh toán",
                CreatedAt = DateTime.Now,
                OrderDetails = cart.CartItems.Select(item => new OrderDetail
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    ExistFirst = products[item.ProductId],
                    SurviveAfter = products[item.ProductId] - (item.Quantity ?? 0)
                }).ToList()
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Store order ID in session for callback
            context.Session.SetInt32("MomoOrderId", order.OrderId);

            var momoModel = new OrderInfo
            {
                FullName = "Khách hàng EasyBuy",
                OrderInfomation = model.OrderDescription,
                Amount = model.Amount
            };

            var response = await _service.CreatePaymentMomo(momoModel);
            
            // Store MOMO order ID for callback verification
            if (response != null && !string.IsNullOrEmpty(response.OrderId))
            {
                context.Session.SetString("MomoOrderStringId", response.OrderId);
            }
            
            return response?.PayUrl ?? "/Order/Checkout";
        }
    }
}