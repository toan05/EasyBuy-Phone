using EasyBuy.Models;

namespace EasyBuy.Services.Discount
{
    public class SeasonalDiscountDecorator : IDiscountCalculator
    {
        private readonly IDiscountCalculator _next;

        public SeasonalDiscountDecorator(IDiscountCalculator next)
        {
            _next = next;
        }

        public decimal CalculateDiscount(decimal total, Voucher? voucher)
        {
            var previous = _next.CalculateDiscount(total, voucher);

            // Example seasonal logic: 5% extra discount for all orders in summer months
            var now = DateTime.Now;
            if (now.Month >= 6 && now.Month <= 8)
            {
                var seasonal = total * 0.05m;
                return Math.Min(total, previous + seasonal);
            }

            return previous;
        }
    }
}