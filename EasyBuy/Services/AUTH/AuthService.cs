using System.Security.Claims;
using EasyBuy.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EasyBuy.Services.AUTH
{
    /// <summary>
    /// Dùng để xác thực người dùng cho khu vực quản trị (Admin/NVKD/NVKho/NVKT/NVMKT).
    /// Các khu vực này đăng nhập bằng Cookie Authentication với scheme "AdminScheme"
    /// (xem Areas/Admin/Controllers/AccountController.cs), vì vậy AuthService phải đọc
    /// thông tin từ claims của scheme đó thay vì Session (Session không được set khi đăng nhập).
    /// </summary>
    public class AuthService : IAuthService
    {
        private const string AdminScheme = "AdminScheme";

        private readonly EasyBuyContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthService(EasyBuyContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        private async Task<ClaimsPrincipal?> GetAdminPrincipalAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return null;

            var result = await httpContext.AuthenticateAsync(AdminScheme);
            return result.Succeeded ? result.Principal : null;
        }

        public async Task<bool> LoginAsync(string email, string password)
        {
            // Việc đăng nhập thực tế được xử lý bằng Cookie Authentication
            // trong Areas/Admin/Controllers/AccountController.cs (SignInAsync "AdminScheme").
            // Giữ phương thức này để tương thích interface.
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (user != null && user.AccountStatus == "Active")
                {
                    return BCrypt.Net.BCrypt.Verify(password, user.Password);
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi đăng nhập: {ex.Message}");
                return false;
            }
        }

        public async Task LogoutAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                await httpContext.SignOutAsync(AdminScheme);
            }
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            var principal = await GetAdminPrincipalAsync();
            return principal?.Identity?.IsAuthenticated == true;
        }

        public async Task<string?> GetCurrentUserRoleAsync()
        {
            var principal = await GetAdminPrincipalAsync();
            return principal?.FindFirst(ClaimTypes.Role)?.Value;
        }

        public async Task<int?> GetCurrentUserIdAsync()
        {
            var principal = await GetAdminPrincipalAsync();
            var idClaim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(idClaim, out var id) ? id : (int?)null;
        }

        public async Task<User?> GetCurrentUserAsync()
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId.HasValue)
            {
                return await _context.Users.FindAsync(userId.Value);
            }
            return null;
        }

        public async Task<bool> HasRoleAsync(string role)
        {
            var currentRole = await GetCurrentUserRoleAsync();
            return currentRole == role;
        }

        public async Task<bool> IsInAnyRoleAsync(params string[] roles)
        {
            var currentRole = await GetCurrentUserRoleAsync();
            return currentRole != null && roles.Contains(currentRole);
        }
    }
}
