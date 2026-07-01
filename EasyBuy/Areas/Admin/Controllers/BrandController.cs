using EasyBuy.Attributes;
using EasyBuy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyBuy.Areas.Admin.Controllers
{
    [Area("Admin")]
    [AuthorizeRole("Admin")]
    public class BrandController : Controller
    {
        private readonly EasyBuyContext _context;

        public BrandController(EasyBuyContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var brands = await _context.Brands.OrderBy(b => b.BrandId).ToListAsync();
            return View(brands);
        }

        public IActionResult Create() => View();

        [HttpPost]
        public async Task<IActionResult> Create(string nameBrand)
        {
            if (string.IsNullOrWhiteSpace(nameBrand))
            {
                TempData["ErrorMessage"] = "Tên thương hiệu không được để trống.";
                return View();
            }

            _context.Brands.Add(new Brand { NameBrand = nameBrand });
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Thêm thương hiệu thành công.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var brand = await _context.Brands.FindAsync(id);
            if (brand == null) return NotFound();
            return View(brand);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, string nameBrand)
        {
            var brand = await _context.Brands.FindAsync(id);
            if (brand == null) return NotFound();

            if (string.IsNullOrWhiteSpace(nameBrand))
            {
                TempData["ErrorMessage"] = "Tên thương hiệu không được để trống.";
                return View(brand);
            }

            brand.NameBrand = nameBrand;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Cập nhật thương hiệu thành công.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var brand = await _context.Brands.FindAsync(id);
            if (brand == null) return NotFound();

            _context.Brands.Remove(brand);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Xóa thương hiệu thành công.";
            return RedirectToAction(nameof(Index));
        }
    }
}
