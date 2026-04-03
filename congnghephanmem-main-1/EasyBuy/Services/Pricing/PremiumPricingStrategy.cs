using EasyBuy.Models;

namespace EasyBuy.Services.Pricing
{
    public class PremiumPricingStrategy : IPricingStrategy
    {
        public decimal CalculatePrice(Product product)
        {
            if (product == null) return 0m;

            var basePrice = product.SellingPrice ?? 0m;
            var premiumMarkup = basePrice * 0.03m;
            return basePrice + premiumMarkup;
        }
    }
}