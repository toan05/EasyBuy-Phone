namespace EasyBuy.Models;

public partial class ProductVariant
{
    public int VariantId { get; set; }
    public int ProductId { get; set; }
    public string? Sku { get; set; }
    public string? Color { get; set; }
    public string? Storage { get; set; }
    public decimal? Price { get; set; }
    public int? Stock { get; set; }

    public virtual Product? Product { get; set; }
}