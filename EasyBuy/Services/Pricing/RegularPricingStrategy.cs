using EasyBuy.Models;

namespace EasyBuy.Services.Pricing
{
    public class RegularPricingStrategy : IPricingStrategy
    {
        public decimal CalculatePrice(Product product)
        {
            return product?.SellingPrice ?? 0m;
        }
    }
}