using EasyBuy.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EasyBuy.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AccountController : Controller
    {
        private readonly EasyBuyContext _context;

        public AccountController(EasyBuyContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Login()
        {
            // Nếu đã đăng nhập với scheme "AdminScheme", chuyển đến dashboard
            var adminAuth = await HttpContext.AuthenticateAsync("AdminScheme");
            if (adminAuth.Succeeded && adminAuth.Principal != null &&
                (adminAuth.Principal.IsInRole("Admin") || adminAuth.Principal.IsInRole("NVKD") || adminAuth.Principal.IsInRole("NVKho")))
            {
                // Chuyển hướng theo vai trò
                return adminAuth.Principal.FindFirstValue(ClaimTypes.Role) switch
                {
                    "NVKD" => RedirectToAction("Index", "Home", new { area = "NVKD" }),
                    "NVKho" => RedirectToAction("Index", "Home", new { area = "NVKho" }),
                    _ => RedirectToAction("Index", "Home", new { area = "Admin" })
                }; 
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string name, string password, bool rememberMe = false, string? returnUrl = null)
        {
            try
            {
                bool isEmail = name.Contains("@");
                var user = isEmail
                    ? _context.Users.FirstOrDefault(u => u.Email == name)
                    : _context.Users.FirstOrDefault(u => u.Phone == name);

                if (user == null || user.Role == "Customer")
                {
                    TempData["Error"] = "Tài khoản không tồn tại hoặc không có quyền truy cập.";
                    return View();
                }

                // Tạm thời bỏ qua kiểm tra khóa tài khoản cho Admin để đơn giản

                bool isPasswordValid = false;
                try
                {
                    isPasswordValid = BCrypt.Net.BCrypt.Verify(password, user.Password);
                }
                catch
                {
                    // Dành cho các mật khẩu cũ chưa được hash
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

                    var claimsIdentity = new ClaimsIdentity(claims, "AdminScheme"); // Sử dụng scheme riêng

                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = rememberMe // Giá trị này sẽ là true nếu người dùng tick vào checkbox
                    };
                    await HttpContext.SignInAsync("AdminScheme", new ClaimsPrincipal(claimsIdentity), authProperties);

                    // Ghi log
                    var log = new LogActivity
                    {
                        UserId = user.UserId,
                        Action = "Đăng nhập Admin",
                        Timestamp = DateTime.Now,
                    };
                    _context.Add(log);
                    await _context.SaveChangesAsync();

                    // Chuyển hướng an toàn
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }

                    // Chuyển hướng theo vai trò
                    return user.Role switch
                    {
                        "Admin" => RedirectToAction("Index", "Home", new { area = "Admin" }),
                        "NVKD" => RedirectToAction("Index", "Home", new { area = "NVKD" }),
                        "NVKho" => RedirectToAction("Index", "Home", new { area = "NVKho" }),
                        _ => RedirectToAction("Index", "Home", new { area = "Admin" })
                    };
                }
                else
                {
                    TempData["Error"] = "Mật khẩu không chính xác.";
                    return View();
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Đã xảy ra lỗi: " + ex.Message;
                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("AdminScheme");
            return RedirectToAction("Login", "Account", new { area = "Admin" });
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}