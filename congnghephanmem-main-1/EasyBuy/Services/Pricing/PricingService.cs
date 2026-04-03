using EasyBuy.Models;

namespace EasyBuy.Services.Pricing
{
    public class PricingService
    {
        private readonly IPricingStrategy _regular;
        private readonly IPricingStrategy _premium;

        public PricingService(RegularPricingStrategy regular, PremiumPricingStrategy premium)
        {
            _regular = regular;
            _premium = premium;
        }

        public decimal CalculatePrice(Product product)
        {
            if (product == null) return 0m;
            if ((product.SellingPrice ?? 0m) > 50000000m)
            {
                return _premium.CalculatePrice(product);
            }
            return _regular.CalculatePrice(product);
        }
    }
}