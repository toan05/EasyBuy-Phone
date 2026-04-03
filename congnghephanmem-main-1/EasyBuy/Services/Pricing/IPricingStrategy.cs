using EasyBuy.Models;

namespace EasyBuy.Services.Pricing
{
    public interface IPricingStrategy
    {
        decimal CalculatePrice(Product product);
    }
}