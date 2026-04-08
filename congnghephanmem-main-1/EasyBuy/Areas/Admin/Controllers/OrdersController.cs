using EasyBuy.Attributes;
using EasyBuy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyBuy.Areas.Admin.Controllers
{
    [Area("Admin")]
    [AuthorizeRole("Admin")]
    public class OrdersController : Controller
    {
        private readonly EasyBuyContext _context;

        public OrdersController(EasyBuyContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> ListOrders()
        {
            try
            {
                var orders = await _context.Orders
                    .Include(o => o.User)
                    .Include(o => o.PaymentMethod)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();
                return View(orders);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi truy xuất CSDL: " + ex.Message;
                return View(new List<Order>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> ViewOrderDetails(int id)
        {
            try
            {
                var order = await _context.Orders
                    .Include(o => o.User)
                    .Include(o => o.Address)
                    .Include(o => o.PaymentMethod)
                    .Include(o => o.Voucher)
                    .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                    .FirstOrDefaultAsync(o => o.OrderId == id);

                if (order == null) return NotFound();
                return View(order);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi truy xuất chi tiết đơn: " + ex.Message;
                return RedirectToAction(nameof(ListOrders));
            }
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmOrder(int id)
        {
            try
            {
                var order = await _context.Orders.FindAsync(id);
                if (order != null && order.Status == "Chờ xác nhận")
                {
                    order.Status = "Đã xác nhận";
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Đã xác nhận đơn hàng thành công!";
                }
                return RedirectToAction("ViewOrderDetails", "Orders", new { area = "Admin", id = id });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi xác nhận đơn: " + ex.Message;
                return RedirectToAction("ViewOrderDetails", "Orders", new { area = "Admin", id = id });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelOrder(int id)
        {
            try
            {
                var order = await _context.Orders.FindAsync(id);
                if (order != null && order.Status == "Chờ xác nhận")
                {
                    order.Status = "Đã hủy";
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Đã hủy đơn hàng thành công!";
                }
                return RedirectToAction("ViewOrderDetails", "Orders", new { area = "Admin", id = id });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi hủy đơn: " + ex.Message;
                return RedirectToAction("ViewOrderDetails", "Orders", new { area = "Admin", id = id });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PrintOrder(int id)
        {
            try
            {
                var order = await _context.Orders
                    .Include(o => o.User)
                    .Include(o => o.Address)
                    .Include(o => o.PaymentMethod)
                    .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                    .FirstOrDefaultAsync(o => o.OrderId == id);

                if (order == null) return NotFound();
                return View(order);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi truy xuất hóa đơn in: " + ex.Message;
                return RedirectToAction(nameof(ListOrders));
            }
        }
    }
}