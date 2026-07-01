using EasyBuy.Models;

namespace EasyBuy.Services.Discount
{
    public interface IDiscountCalculator
    {
        decimal CalculateDiscount(decimal total, Voucher? voucher);
    }
}