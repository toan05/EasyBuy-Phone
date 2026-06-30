﻿﻿using EasyBuy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;


namespace EasyBuy.Controllers
{
    public class AccountController : Controller
    {
        private readonly EasyBuyContext _context;
        private EasyBuy.Method.Method method = new EasyBuy.Method.Method();
        
        public AccountController(EasyBuyContext context)
        {
            _context = context;
        }
        [HttpGet]
        public IActionResult Login()
        {
            // Sử dụng User.Identity.IsAuthenticated để kiểm tra đăng nhập từ cookie
            if (User.Identity != null && User.Identity.IsAuthenticated && User.Identity.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme)
            {
                return RedirectToAction("TrangChu", "Home");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string name, string password, bool rememberMe = false)
        {
            try
            {
                bool isEmail = name.Contains("@");
                var user = isEmail
                    ? _context.Users.FirstOrDefault(u => u.Email == name)
                    : _context.Users.FirstOrDefault(u => u.Phone == name);

                if (user == null)
                {
                    ViewBag.Error = "Tài khoản không tồn tại";
                    return View();
                }

                if (user.AccountStatus == "Locked" && user.LockedAt != null)
                {
                    var minutesLocked = (DateTime.Now - user.LockedAt.Value).TotalMinutes;
                    if (minutesLocked >= 15)
                    {
                        user.AccountStatus = "Active";
                        user.FailedLoginCount = 0;
                        user.LockedAt = null;
                        _context.SaveChanges();
                    }
                    else
                    {
                        ViewBag.Error = $"Tài khoản bị khóa. Vui lòng thử lại sau {Math.Ceiling(15 - minutesLocked)} phút.";
                        return View();
                    }
                }
                else if (user.AccountStatus == "Locked")
                {
                    ViewBag.Error = "Tài khoản đã bị khóa.";
                    return View();
                }

                bool isPasswordValid = false;
                try
                {
                    isPasswordValid = BCrypt.Net.BCrypt.Verify(password, user.Password);
                }
                catch
                {
                    // Nếu mật khẩu trong DB chưa được mã hóa (tạo từ Admin)
                    isPasswordValid = (password == user.Password);
                    if (isPasswordValid)
                    {
                        // Mã hóa lại mật khẩu và lưu vào DB để đảm bảo an toàn cho lần sau
                        user.Password = BCrypt.Net.BCrypt.HashPassword(password);
                        _context.SaveChanges();
                    }
                }

                if (isPasswordValid)
                {
                    // Chỉ cho phép Customer đăng nhập ở đây
                    if (user.Role != "Customer")
                    {
                        ViewBag.Error = "Vui lòng sử dụng trang đăng nhập dành cho quản trị viên.";
                        return View("Login");
                    }

                    // Tạo các claims để lưu vào cookie
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, user.Email ?? user.Phone),
                        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                        new Claim(ClaimTypes.Role, user.Role)
                    };
 
                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = rememberMe // Giá trị này sẽ là true nếu người dùng tick vào checkbox
                    };
 
                    // Đăng nhập bằng cookie
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);
 
                    // Reset số lần đăng nhập sai
                    user.FailedLoginCount = 0;
                    _context.SaveChanges();
 
                    // Ghi log
                    var log = new LogActivity
                    {
                        UserId = user.UserId,
                        Action = "Đăng nhập",
                        Timestamp = DateTime.Now,
                    };
                    _context.Add(log);
                    _context.SaveChanges();
 
                    return RedirectToAction("TrangChu", "Home");
                }
                else
                {
                    user.FailedLoginCount++;

                    if (user.FailedLoginCount >= 3)
                    {
                        user.AccountStatus = "Locked";
                        user.LockedAt = DateTime.Now;
                        ViewBag.Error = "Tài khoản đã bị khóa do đăng nhập sai nhiều lần.";
                    }
                    else
                    {
                        ViewBag.Error = "Mật khẩu không chính xác.";
                    }

                    _context.SaveChanges();
                    return View();
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Đã xảy ra lỗi trong quá trình đăng nhập: " + ex.Message;
                return View();
            }
        }


        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            try
            {
                // Chỉ đăng xuất khỏi scheme của customer
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Chỉ đăng xuất scheme của customer
 
                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                // Hiển thị lỗi hoặc xử lý khác
                TempData["Error"] = "Đăng xuất thất bại: " + ex.Message;
                return RedirectToAction("TrangChu", "Home");
            }
        }

        [HttpGet]
        public IActionResult Register()
        {
            // Kiểm tra đăng nhập bằng cookie
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("TrangChu", "Home");
            }
            return View();
        }
        [HttpPost]

        public IActionResult Register(string phone, string password, string repassword, string name, string email)
        {
            try
            {
                if (method.IsEmpty(phone) || method.IsEmpty(password) || method.IsEmpty(repassword) || method.IsEmpty(name) || method.IsEmpty(email))
                {
                    ViewBag.MS = "Các trường không được để trống";
                    return View();
                }
                if (!method.IsValidPassword(password))
                {
                    ViewBag.MS = "Mật khẩu phải lớn hơn 8 ký tự và có chữ hoa chữ thường";
                    return View();
                }
                if (!method.IsValidVietnamPhoneNumber(phone))
                {
                    ViewBag.MS = "Số điện thoại không hợp lệ";
                    return View();
                }
                if (password != repassword)
                {
                    ViewBag.MS = "Mật khẩu nhập lại không đúng";
                    return View();
                }
                if (!method.IsValidName(name))
                {
                    ViewBag.MS = "Tên không được chứa số hay ký tự đặc biệt";
                    return View();
                }
                if (!method.IsValidEmail(email))
                {
                    ViewBag.MS = "Email không hợp lệ";
                    return View();
                }
                if (_context.Users.Any(u => u.Phone == phone))
                {
                    ViewBag.MS = "Số điện thoại đã có người sử dụng";
                    return View();
                }

                if (_context.Users.Any(u => u.Email == email))
                {
                    ViewBag.MS = "Email đã có người sử dụng";
                    return View();
                }
                var user = new User
                {
                    Phone = phone,
                    Email = email,
                    Password = BCrypt.Net.BCrypt.HashPassword(password),
                    FullName = name,
                    FailedLoginCount = 0,
                    AccountStatus = "Active",
                    Role = "Customer",
                    CreatedAt = DateTime.Now
                };
                _context.Add(user);
                _context.SaveChanges();
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                ViewBag.MS = "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau." + ex;
                return View();
            }
        }

        [HttpGet]
        public IActionResult UpdateAccount()
        {
            // Kiểm tra đăng nhập bằng cookie
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account");
            }
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized();
            }

            var user = _context.Users.Find(userId);

            // Truyền model user vào View
            return View(user);
        }

        [HttpPost]
        public IActionResult UpdateAccount(string name, string email, string phone, string password)
        {
            var userToUpdateView = new User { FullName = name, Email = email, Phone = phone };
            try
            {   
                // Lấy userId từ Claims thay vì Session
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    return Unauthorized();
                }
 
                var user = _context.Users.Find(userId);
 
                if (method.IsEmpty(password))
                {
                    ViewBag.Error = "Bạn phải nhập mật khẩu để xác nhận thay đổi.";
                    return View(user);
                }
                bool isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.Password);
                if (!isValidPassword)
                {
                    ViewBag.Error = "Mật khẩu không đúng.";
                    return View(user);
                }
 
                // Chỉ cập nhật trường nào được nhập
                if (!method.IsEmpty(name))
                {
                    user.FullName = name;
                }
                if (!method.IsEmpty(email))
                {
                    if (!method.IsValidEmail(email))
                    {
                        ViewBag.Error = "Email không hợp lệ";
                        return View(user);
                    }
                    if (_context.Users.Any(u => u.Email == email && u.UserId != user.UserId))
                    {
                        ViewBag.Error = "Email đã có người sử dụng";
                        return View(user);
                    }
                    user.Email = email;
                }
                if (!method.IsEmpty(phone))
                {
                    if (!method.IsValidVietnamPhoneNumber(phone))
                    {
                        ViewBag.Error = "Số điện thoại không hợp lệ";
                        return View(user);
                    }
                    if (_context.Users.Any(u => u.Phone == phone && u.UserId != user.UserId))
                    {
                        ViewBag.Error = "Số điện thoại đã có người sử dụng";
                        return View(user);
                    }
                    user.Phone = phone;
                }
 
                _context.SaveChanges();
 
                ViewBag.Success = "Cập nhật tài khoản thành công!";
                return View(user);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                ViewBag.Error = "Có lỗi hệ thống";
                return View(userToUpdateView);
            }
        }


        [HttpGet]
        public IActionResult ChangePassword()
        {
            // Kiểm tra đăng nhập bằng cookie
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account");
            }
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized();
            }

            var user = _context.Users.Find(userId);

            return View(user);
        }

        [HttpPost]
        public IActionResult ChangePassword(string newpassword, string renewpassword, string password)
        {
            var user = _context.Users.Find(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)));
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    return Unauthorized();
                }
 
                if (user == null)
                {
                    return RedirectToAction("Login", "Account");
                }
 
                if (method.IsEmpty(newpassword) || method.IsEmpty(renewpassword) || method.IsEmpty(password))
                {
                    ViewBag.Error = "Các trường không được để trống";
                    return View(user);
                }
                bool isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.Password);
                if (!isValidPassword)
                {
                    ViewBag.Error = "Mật khẩu hiện tại không đúng.";
                    return View(user);
                }
                if (newpassword != renewpassword)
                {
                    ViewBag.Error = "Mật khẩu nhập lại không đúng";
                    return View(user);
                }
                if (!method.IsValidPassword(newpassword))
                {
                    ViewBag.Error = "Mật khẩu phải lớn hơn 8 ký tự và có chữ hoa chữ thường";
                    return View(user);
                }
                user.Password = BCrypt.Net.BCrypt.HashPassword(newpassword);
                _context.SaveChanges();
                ViewBag.Success = "Đổi mật khẩu thành công!";
                return View(user);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Lỗi hệ thống: " + ex.Message;
                return View(user); // Trả về user đã tìm thấy ở đầu hàm
            }
        }

        [HttpGet]
        public IActionResult ListAdress()
        {
            // Lấy userId từ Claims thay vì Session
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }
 
            var addresses = _context.Addresses
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .ToList();
 
            return View(addresses);
        }
        [HttpGet]
        public IActionResult AddAdress()
        {
            // Kiểm tra đăng nhập bằng cookie thay vì session
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {   
                return RedirectToAction("Login");
            }
            return View();
        }

        [HttpPost]
        public IActionResult AddAdress(string city, string district, string ward, string street, string phone, string recipientName)
        {
            try
            {
                // Lấy userId từ Claims thay vì Session
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    return RedirectToAction("Login", "Account");
                }
 
                var user = _context.Users.Find(userId);
                if (user == null)
                {
                    return RedirectToAction("Login", "Account");
                }
 
                // Tạo model để giữ lại dữ liệu khi có lỗi
                var model = new Address
                {
                    City = city,
                    District = district,
                    Ward = ward,
                    Street = street,
                    Phone = phone,
                    RecipientName = recipientName
                };
 
                if (method.IsEmpty(city) || method.IsEmpty(district) || method.IsEmpty(ward) || method.IsEmpty(street) || method.IsEmpty(phone) || method.IsEmpty(recipientName))
                {
                    ViewBag.MS = "Các trường không được bỏ trống";
                    return View(model);
                }
 
                if (!method.IsValidVietnamPhoneNumber(phone))
                {
                    ViewBag.MS = "Số điện thoại không hợp lệ";
                    return View(model);
                }
 
                var address = new Address
                {
                    City = city,
                    District = district,
                    Ward = ward,
                    Street = street,
                    Phone = phone,
                    RecipientName = recipientName,
                    CreatedAt = DateTime.Now, 
                    UserId = userId
                };
                _context.Add(address);
                _context.SaveChanges();
                TempData["Success"] = "Thêm địa chỉ thành công!";
                return RedirectToAction("ListAdress");
            }
            catch (Exception ex)
            {
                ViewBag.MS = "Lỗi hệ thống: " + ex.Message;
                // Trả lại dữ liệu đã nhập
                var model = new Address
                {
                    City = city,
                    District = district,
                    Ward = ward,
                    Street = street,
                    Phone = phone
                };
                return View(model);
            }
        }
        [HttpPost]
        public IActionResult DeleteAddress(int id)
        {
            try
            {
                // Lấy userId từ Claims thay vì Session
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    return RedirectToAction("Login", "Account");
                }
 
                var address = _context.Addresses.FirstOrDefault(a => a.AddressId == id && a.UserId == userId);
                if (address == null)
                {
                    TempData["MS"] = "Không tìm thấy địa chỉ hoặc bạn không có quyền xóa.";
                    return RedirectToAction("ListAdress");
                }
 
                _context.Addresses.Remove(address);
                _context.SaveChanges();
                TempData["Success"] = "Xóa địa chỉ thành công!";
                return RedirectToAction("ListAdress");
            }
            catch
            {
                TempData["MS"] = "Không thể xóa địa chỉ này";
                return RedirectToAction("ListAdress");
            }
        }
        [HttpGet]
        public IActionResult UpdateAddress(int id)
        {
            // Lấy userId từ Claims thay vì Session
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }
 
            var address = _context.Addresses.FirstOrDefault(a => a.AddressId == id && a.UserId == userId);
            if (address == null)
            {
                TempData["MS"] = "Không tìm thấy địa chỉ hoặc bạn không có quyền sửa.";
                return RedirectToAction("ListAdress");
            }
 
            return View(address);
        }

        [HttpPost]
        public IActionResult UpdateAddress(int id, string city, string district, string ward, string street, string phone, string recipientName)
        {
            try
            {
                // Lấy userId từ Claims thay vì Session
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    return RedirectToAction("Login", "Account");
                }
 
                var address = _context.Addresses.FirstOrDefault(a => a.AddressId == id && a.UserId == userId);
                if (address == null)
                {
                    TempData["MS"] = "Không tìm thấy địa chỉ hoặc bạn không có quyền sửa.";
                    return RedirectToAction("ListAdress");
                }
 
                if (method.IsEmpty(city) || method.IsEmpty(district) || method.IsEmpty(ward) || method.IsEmpty(street) || method.IsEmpty(phone) || method.IsEmpty(recipientName))
                {
                    ViewBag.MS = "Các trường không được bỏ trống";
                    return View(address);
                }
 
                if (!method.IsValidVietnamPhoneNumber(phone))
                {
                    ViewBag.MS = "Số điện thoại không hợp lệ";
                    return View(address);
                }
 
                // Cập nhật thông tin
                address.City = city;
                address.District = district;
                address.Ward = ward;
                address.Street = street;
                address.Phone = phone;
                address.RecipientName = recipientName;
 
                _context.SaveChanges();
                TempData["Success"] = "Cập nhật địa chỉ thành công!";
                return RedirectToAction("ListAdress");
            }
            catch (Exception ex)
            {
                TempData["MS"] = "Lỗi hệ thống: " + ex.Message;
                return RedirectToAction("ListAdress");
            }
        }

        [HttpPost]
        public async Task<IActionResult> LockedAccount(bool confirm)
        {
            // Lấy userId từ Claims thay vì Session
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }
            var user = await _context.Users.FindAsync(userId); // userId đã được parse an toàn
            if (user == null)
            {
                TempData["MS"] = "Không tìm thấy tài khoản.";
                return RedirectToAction("Login", "Account");
            }
 
            if (confirm)
            {
                user.AccountStatus = "Locked";
                // Đăng xuất khỏi cookie
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Khóa tài khoản thành công!";
            }
            else
            {
                TempData["MS"] = "Bạn đã hủy thao tác khóa tài khoản.";
            }
            return RedirectToAction("Login", "Account");
        }
 
        public async Task<IActionResult> LoginByGoogle()
        {
            await HttpContext.ChallengeAsync(
                GoogleDefaults.AuthenticationScheme,
                new AuthenticationProperties
                {
                    RedirectUri = Url.Action("GoogleResponse")
                });
            return new EmptyResult();
        }
 
        public async Task<IActionResult> GoogleResponse()
        {
            try
            {
                var result = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
                if (!result.Succeeded)
                {
                    TempData["Error"] = "Đăng nhập Google thất bại. Vui lòng thử lại.";
                    return RedirectToAction("Login");
                }
 
                var claims = result.Principal.Identities.FirstOrDefault()?.Claims;
                var email = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
                var fullName = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
 
                if (string.IsNullOrEmpty(email))
                {
                    TempData["Error"] = "Không thể lấy thông tin email từ Google. Vui lòng thử lại.";
                    return RedirectToAction("Login");
                }
 
                // Kiểm tra user đã tồn tại
                var existingUser = _context.Users.FirstOrDefault(u => u.Email == email);
                
                if (existingUser != null)
                {
                    // Kiểm tra trạng thái tài khoản
                    if (existingUser.AccountStatus == "Locked" && existingUser.LockedAt != null)
                    {
                        var minutesLocked = (DateTime.Now - existingUser.LockedAt.Value).TotalMinutes;
                        if (minutesLocked >= 15)
                        {
                            existingUser.AccountStatus = "Active";
                            existingUser.FailedLoginCount = 0;
                            existingUser.LockedAt = null;
                            _context.SaveChanges();
                        }
                        else
                        {
                            TempData["Error"] = $"Tài khoản bị khóa. Vui lòng thử lại sau {Math.Ceiling(15 - minutesLocked)} phút.";
                            return RedirectToAction("Login");
                        }
                    }
                    else if (existingUser.AccountStatus == "Locked")
                    {
                        TempData["Error"] = "Tài khoản đã bị khóa.";
                        return RedirectToAction("Login");
                    }
 
                    // Đăng nhập thành công bằng CustomerScheme
                    var loginClaims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, existingUser.Email),
                        new Claim(ClaimTypes.NameIdentifier, existingUser.UserId.ToString()),
                        new Claim(ClaimTypes.Role, existingUser.Role)
                    };
                    var claimsIdentity = new ClaimsIdentity(loginClaims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), new AuthenticationProperties { IsPersistent = true });
 
                    existingUser.FailedLoginCount = 0;
                    
                    var log = new LogActivity
                    {
                        UserId = existingUser.UserId,
                        Action = "Đăng nhập bằng Google",
                        Timestamp = DateTime.Now,
                    };
                    _context.Add(log);
                    await _context.SaveChangesAsync();
 
 
                    return RedirectToAction("TrangChu", "Home");
                }
                else
                {
                    // Nếu user chưa tồn tại, chuyển đến trang đặt mật khẩu
                    // Lưu tạm thông tin vào TempData thay vì Session
                    TempData["GoogleEmail"] = email;
                    TempData["GoogleName"] = string.IsNullOrEmpty(fullName) ? email.Split('@')[0] : fullName;
                    return RedirectToAction("SetPassword");
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Đã xảy ra lỗi trong quá trình đăng nhập Google: " + ex.Message;
                return RedirectToAction("Login");
            }
        }
 
        [HttpGet]
        public IActionResult SetPassword()
        {
            string? email = TempData["GoogleEmail"] as string;
            string? name = TempData["GoogleName"] as string;
            if (string.IsNullOrEmpty(email))
            {
                TempData["Error"] = "Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại.";
                return RedirectToAction("Login");
            }
            
            ViewBag.Email = email;
            ViewBag.UserName = name;
            // Giữ lại TempData cho lần POST
            TempData.Keep("GoogleEmail");
            TempData.Keep("GoogleName");
            return View();
        }
 
        [HttpPost]
        public async Task<IActionResult> SetPassword(string password, string confirmPassword)
        {
            try
            {
                string? email = TempData["GoogleEmail"] as string;
                string? name = TempData["GoogleName"] as string;
                if (string.IsNullOrEmpty(email))
                {
                    TempData["Error"] = "Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại.";
                    return RedirectToAction("Login");
                }
 
                ViewBag.Email = email;
                ViewBag.UserName = name;
                TempData.Keep("GoogleEmail");
                TempData.Keep("GoogleName");
 
                // Validation
                if (method.IsEmpty(password) || method.IsEmpty(confirmPassword))
                {
                    ViewBag.Error = "Vui lòng nhập đầy đủ thông tin.";
                    return View();
                }

                if (password != confirmPassword)
                {
                    ViewBag.Error = "Mật khẩu xác nhận không khớp.";
                    return View();
                }
 
                if (!method.IsValidPassword(password))
                {
                    ViewBag.Error = "Mật khẩu phải có ít nhất 8 ký tự, bao gồm chữ hoa và chữ thường.";
                    return View();
                }
 
                // Kiểm tra email đã tồn tại chưa (tránh trường hợp user tạo account trong khi đang đặt password)
                var existingUser = _context.Users.FirstOrDefault(u => u.Email == email);
                if (existingUser != null)
                {
                    TempData["Error"] = "Email này đã được sử dụng. Vui lòng đăng nhập bình thường.";
                    return RedirectToAction("Login");
                }
 
                // Tạo user mới
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
                var newUser = new User
                {
                    Email = email,
                    FullName = name,
                    Phone = null,
                    Password = passwordHash,
                    Role = "Customer",
                    AccountStatus = "Active",
                    FailedLoginCount = 0,
                    CreatedAt = DateTime.Now,
                };
 
                _context.Add(newUser);
                _context.SaveChanges();
 
                // Đăng nhập tự động sau khi tạo tài khoản
                var newClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, newUser.Email),
                    new Claim(ClaimTypes.NameIdentifier, newUser.UserId.ToString()),
                    new Claim(ClaimTypes.Role, newUser.Role)
                };
                var newClaimsIdentity = new ClaimsIdentity(newClaims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(newClaimsIdentity), new AuthenticationProperties { IsPersistent = true });
 
                
                // Ghi log đăng ký
                var logRegister = new LogActivity
                {
                    UserId = newUser.UserId,
                    Action = "Đăng ký bằng Google",
                    Timestamp = DateTime.Now,
                };
                _context.Add(logRegister);
                _context.SaveChanges();
 
                TempData["Success"] = "Tạo tài khoản thành công! Chào mừng bạn đến với EasyBuy.";
                return RedirectToAction("TrangChu", "Home");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Đã xảy ra lỗi: " + ex.Message;
                return View();
            }
        }
    }
}
