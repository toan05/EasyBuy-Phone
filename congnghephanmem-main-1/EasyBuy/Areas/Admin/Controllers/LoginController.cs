using EasyBuy.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EasyBuy.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class LoginController : Controller
    {
        private readonly EasyBuyContext _context;

        public LoginController(EasyBuyContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index()
        {
            // Nếu đã đăng nhập bằng AdminScheme, chuyển hướng vào trang chủ Admin
            if (User.Identity.IsAuthenticated && User.Identity.AuthenticationType == "AdminScheme")
            {
                return RedirectToAction("Index", "Home", new { area = "Admin" });
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Index(string name, string password)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Vui lòng nhập email/số điện thoại và mật khẩu.";
                return View();
            }

            bool isEmail = name.Contains("@");
            var user = isEmail
                ? await _context.Users.FirstOrDefaultAsync(u => u.Email == name)
                : await _context.Users.FirstOrDefaultAsync(u => u.Phone == name);

            if (user == null)
            {
                ViewBag.Error = "Tài khoản không tồn tại.";
                return View();
            }

            // Trang này chỉ dành cho các vai trò quản trị
            var allowedRoles = new[] { "Admin", "NVKD", "NVKho", "NVMKT" };
            if (!allowedRoles.Contains(user.Role))
            {
                ViewBag.Error = "Tài khoản của bạn không có quyền truy cập vào khu vực này.";
                return View();
            }

            bool isPasswordValid = false;
            try
            {
                isPasswordValid = BCrypt.Net.BCrypt.Verify(password, user.Password);
            }
            catch
            {
                isPasswordValid = (password == user.Password);
                if (isPasswordValid)
                {
                    user.Password = BCrypt.Net.BCrypt.HashPassword(password);
                    await _context.SaveChangesAsync();
                }
            }

            if (isPasswordValid)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Email ?? user.Phone),
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new Claim(ClaimTypes.Role, user.Role)
                };

                var claimsIdentity = new ClaimsIdentity(claims, "AdminScheme");
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true
                };

                await HttpContext.SignInAsync("AdminScheme", new ClaimsPrincipal(claimsIdentity), authProperties);

                // Ghi log
                var log = new LogActivity
                {
                    UserId = user.UserId,
                    Action = "Đăng nhập trang quản trị",
                    Timestamp = DateTime.Now,
                };
                _context.Add(log);
                await _context.SaveChangesAsync();

                // Chuyển hướng dựa trên vai trò
                return user.Role switch
                {
                    "Admin" => RedirectToAction("Index", "Home", new { area = "Admin" }),
                    "NVKD" => RedirectToAction("Index", "Home", new { area = "NVKD" }),
                    "NVKho" => RedirectToAction("Index", "Home", new { area = "NVKho" }),
                    "NVMKT" => RedirectToAction("Index", "Home", new { area = "NVMKT" }),
                    _ => RedirectToAction("Index", "Home", new { area = "Admin" })
                };
            }

            ViewBag.Error = "Mật khẩu không chính xác.";
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("AdminScheme");
            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}