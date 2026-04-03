using EasyBuy.Models;

namespace EasyBuy.Services.Discount
{
    public class BaseDiscountCalculator : IDiscountCalculator
    {
        public decimal CalculateDiscount(decimal total, Voucher? voucher)
        {
            return 0m;
        }
    }
}