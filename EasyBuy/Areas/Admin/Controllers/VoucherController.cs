using EasyBuy.Attributes;
using EasyBuy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyBuy.Areas.Admin.Controllers
{
    [Area("Admin")]
    [AuthorizeRole("Admin")]
    public class VoucherController : Controller
    {
        private readonly EasyBuyContext _context;

        public VoucherController(EasyBuyContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var vouchers = await _context.Vouchers.OrderBy(v => v.VoucherId).ToListAsync();
            return View(vouchers);
        }

        public IActionResult Create() => View();

        [HttpPost]
        public async Task<IActionResult> Create(string code, decimal discountValue, DateOnly startDate, DateOnly endDate, bool isActive = true)
        {
            if (string.IsNullOrWhiteSpace(code) || discountValue <= 0 || startDate >= endDate)
            {
                TempData["ErrorMessage"] = "Vui lòng nhập đầy đủ và hợp lệ thông tin khuyến mãi.";
                return View();
            }

            var exist = await _context.Vouchers.AnyAsync(v => v.Code == code);
            if (exist)
            {
                TempData["ErrorMessage"] = "Mã khuyến mãi đã tồn tại.";
                return View();
            }

            _context.Vouchers.Add(new Voucher
            {
                Code = code.Trim(),
                DiscountValue = discountValue,
                StartDate = startDate,
                EndDate = endDate,
                IsActive = isActive,
                CreatedAt = DateTime.Now
            });
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Thêm khuyến mãi thành công.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var voucher = await _context.Vouchers.FindAsync(id);
            if (voucher == null) return NotFound();
            return View(voucher);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, string code, decimal discountValue, DateOnly startDate, DateOnly endDate, bool isActive)
        {
            var voucher = await _context.Vouchers.FindAsync(id);
            if (voucher == null) return NotFound();

            if (string.IsNullOrWhiteSpace(code) || discountValue <= 0 || startDate >= endDate)
            {
                TempData["ErrorMessage"] = "Vui lòng nhập đầy đủ và hợp lệ thông tin khuyến mãi.";
                return View(voucher);
            }

            voucher.Code = code.Trim();
            voucher.DiscountValue = discountValue;
            voucher.StartDate = startDate;
            voucher.EndDate = endDate;
            voucher.IsActive = isActive;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Cập nhật khuyến mãi thành công.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var voucher = await _context.Vouchers.FindAsync(id);
            if (voucher == null) return NotFound();

            _context.Vouchers.Remove(voucher);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Xóa khuyến mãi thành công.";
            return RedirectToAction(nameof(Index));
        }
    }
}
