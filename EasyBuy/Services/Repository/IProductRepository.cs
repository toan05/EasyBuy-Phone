using EasyBuy.Models;
using System.Threading.Tasks;

namespace EasyBuy.Services.Repository
{
    public interface IProductRepository : IRepository<Product>
    {
        Task<Product?> GetByNameAsync(string name);
    }
}