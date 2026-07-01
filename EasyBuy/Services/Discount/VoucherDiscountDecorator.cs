using EasyBuy.Models;

namespace EasyBuy.Services.Discount
{
    public class VoucherDiscountDecorator : IDiscountCalculator
    {
        private readonly IDiscountCalculator _next;

        public VoucherDiscountDecorator(IDiscountCalculator next)
        {
            _next = next;
        }

        public decimal CalculateDiscount(decimal total, Voucher? voucher)
        {
            var previous = _next.CalculateDiscount(total, voucher);
            if (voucher == null || !voucher.IsActive.GetValueOrDefault() || voucher.Quantity.GetValueOrDefault() <= 0)
                return previous;

            decimal discount = 0m;
            if (voucher.DiscountType == "percent")
            {
                discount = total * ((voucher.DiscountValue ?? 0m) / 100m);
            }
            else
            {
                discount = voucher.DiscountValue ?? 0m;
            }

            if (voucher.MaxDiscountAmount.HasValue && discount > voucher.MaxDiscountAmount.Value)
                discount = voucher.MaxDiscountAmount.Value;

            return Math.Min(total, previous + discount);
        }
    }
}