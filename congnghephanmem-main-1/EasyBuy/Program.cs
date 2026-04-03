using EasyBuy.Models;
using EasyBuy.Models.MOMO;
using EasyBuy.Library;
using EasyBuy.Services.AUTH;
using EasyBuy.Services.EMAILOTP;
using EasyBuy.Services.MOMO;
using EasyBuy.Services.Command;
using EasyBuy.Services.Discount;
using EasyBuy.Services.Observers;
using EasyBuy.Services.Payment;
using EasyBuy.Services.Pricing;
using EasyBuy.Services.Repository;
using EasyBuy.Services.SIMPLECHAT;
using EasyBuy.Services.VNPAY;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ==========================================================
// 1. ĐĂNG KÝ DESIGN PATTERNS (Logger, Factory, Observer)
// ==========================================================
builder.Services.AddSingleton<MyLogger>(MyLogger.Instance);
builder.Services.AddScoped<PaymentFactory>();
builder.Services.AddScoped<OrderSubject>(provider => {
    var subject = new OrderSubject();
    var emailService = provider.GetRequiredService<IEmailService>();
    subject.Attach(new EmailNotificationObserver(emailService));
    subject.Attach(new AdminNotificationObserver());
    return subject;
});

// ==========================================================
// 2. ĐĂNG KÝ CÁC DỊCH VỤ HỆ THỐNG
// ==========================================================
builder.Services.AddDbContext<EasyBuyContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptionsAction: sqlOptions => {
            sqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
        }));

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IVnPayService, VnPayService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<LearningService>();
builder.Services.AddScoped<SimpleChatService>();
builder.Services.Configure<MomoOptionModel>(builder.Configuration.GetSection("MomoAPI"));
builder.Services.AddScoped<IMomoService, MomoService>();

// Strategy: pricing service
builder.Services.AddScoped<RegularPricingStrategy>();
builder.Services.AddScoped<PremiumPricingStrategy>();
builder.Services.AddScoped<PricingService>();

// Decorator: discount
builder.Services.AddScoped<IDiscountCalculator, BaseDiscountCalculator>();

// Repository:
builder.Services.AddScoped<IProductRepository, ProductRepository>();

// Command: order
builder.Services.AddScoped<IOrderCommand, CreateOrderCommand>();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

// ==========================================================
// 3. AUTHENTICATION & SESSION (QUAN TRỌNG CHO PHÂN QUYỀN)
// ==========================================================
builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromMinutes(40);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthentication(options => {
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options => {
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
})
.AddGoogle(options => {
    options.ClientId = builder.Configuration.GetSection("GoogleKeys:ClientId").Value;
    options.ClientSecret = builder.Configuration.GetSection("GoogleKeys:ClientSecret").Value;
    options.CallbackPath = "/signin-google"; // Thêm để đảm bảo login Google ko lỗi
});

var app = builder.Build();

// ==========================================================
// SEED DATA (THÊM DỮ LIỆU MẪU NẾU DATABASE TRỐNG)
// ==========================================================
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<EasyBuyContext>();
    SeedData(context);
}

// ==========================================================
// 4. KÍCH HOẠT LOGGER & MIDDLEWARE (THỨ TỰ LÀ SỐNG CÒN)
// ==========================================================
var logger = app.Services.GetRequiredService<MyLogger>();
logger.Log("Hệ thống EasyBuy đã khởi động!");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();        // Phải nằm trước Authentication
app.UseAuthentication(); // Phải nằm trước Authorization
app.UseAuthorization();

// ==========================================================
// 5. ĐỊNH NGHĨA ROUTING (DÒNG AREAS PHẢI NẰM TRÊN CÙNG)
// ==========================================================

// --- DÒNG QUAN TRỌNG ĐỂ SỬA LỖI 404 ADMIN ---
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "productDetails",
    pattern: "product/{productId:int}",
    defaults: new { controller = "Home", action = "ViewProductDetails" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=TrangChu}/{id?}");

app.Run();

// ==========================================================
// SEED DATA METHOD
// ==========================================================
void SeedData(EasyBuyContext context)
{
    if (context.Users.Any() || context.Categories.Any() || context.Brands.Any() || context.Products.Any() || context.PaymentMethods.Any())
        return; // Đã có dữ liệu

    // Seed Payment Methods
    var paymentMethods = new[]
    {
        new PaymentMethod
        {
            MethodName = "Thanh toán khi nhận hàng (COD)",
            Description = "Thanh toán bằng tiền mặt khi nhận hàng",
            IsActive = true,
            CreatedAt = DateTime.Now
        },
        new PaymentMethod
        {
            MethodName = "Thanh toán qua VNPay",
            Description = "Thanh toán online qua cổng VNPay",
            IsActive = true,
            CreatedAt = DateTime.Now
        },
        new PaymentMethod
        {
            MethodName = "Thanh toán qua MoMo",
            Description = "Thanh toán online qua ví điện tử MoMo",
            IsActive = true,
            CreatedAt = DateTime.Now
        }
    };
    context.PaymentMethods.AddRange(paymentMethods);
    context.SaveChanges();

    // Seed Brands
    var brands = new[]
    {
        new Brand { NameBrand = "Apple" },
        new Brand { NameBrand = "Samsung" },
        new Brand { NameBrand = "Nike" },
        new Brand { NameBrand = "Adidas" }
    };
    context.Brands.AddRange(brands);
    context.SaveChanges();

    // Seed Categories
    var categories = new[]
    {
        new Category { CategoryName = "Điện thoại" },
        new Category { CategoryName = "Laptop" },
        new Category { CategoryName = "Giày dép" },
        new Category { CategoryName = "Quần áo" }
    };
    context.Categories.AddRange(categories);
    context.SaveChanges();

    // Seed Users (bao gồm admin và user thường)
    var users = new[]
    {
        new User
        {
            FullName = "Admin",
            Email = "admin@easybuy.com",
            Password = BCrypt.Net.BCrypt.HashPassword("admin123"),
            Phone = "0123456789",
            Role = "Admin",
            AccountStatus = "Active",
            CreatedAt = DateTime.Now
        },
        new User
        {
            FullName = "Nguyễn Văn A",
            Email = "user@easybuy.com",
            Password = BCrypt.Net.BCrypt.HashPassword("user123"),
            Phone = "0987654321",
            Role = "Customer",
            AccountStatus = "Active",
            CreatedAt = DateTime.Now
        }
    };
    context.Users.AddRange(users);
    context.SaveChanges();

    // Seed Products
    var products = new[]
    {
        new Product
        {
            ProductName = "iPhone 15 Pro",
            Description = "Điện thoại cao cấp của Apple",
            SellingPrice = 25000000,
            Quantity = 10,
            ImagePr = "/images/iphone15.jpg",
            CateId = categories[0].CateId, // Điện thoại
            BrandId = brands[0].BrandId, // Apple
            StatusProduct = "Active",
            Barcode = "IPHONE15PRO001"
        },
        new Product
        {
            ProductName = "Samsung Galaxy S24",
            Description = "Điện thoại flagship của Samsung",
            SellingPrice = 20000000,
            Quantity = 15,
            ImagePr = "/images/galaxy-s24.jpg",
            CateId = categories[0].CateId,
            BrandId = brands[1].BrandId, // Samsung
            StatusProduct = "Active",
            Barcode = "SAMSUNGS24ULTRA001"
        },
        new Product
        {
            ProductName = "MacBook Pro 16\"",
            Description = "Laptop chuyên nghiệp của Apple",
            SellingPrice = 50000000,
            Quantity = 5,
            ImagePr = "/images/macbook-pro.jpg",
            CateId = categories[1].CateId, // Laptop
            BrandId = brands[0].BrandId,
            StatusProduct = "Active",
            Barcode = "MACBOOKPRO16INCH001"
        },
        new Product
        {
            ProductName = "Nike Air Max",
            Description = "Giày thể thao Nike",
            SellingPrice = 3000000,
            Quantity = 20,
            ImagePr = "/images/nike-airmax.jpg",
            CateId = categories[2].CateId, // Giày dép
            BrandId = brands[2].BrandId, // Nike
            StatusProduct = "Active",
            Barcode = "NIKEAIRMAX001"
        }
    };
    context.Products.AddRange(products);
    context.SaveChanges();

    Console.WriteLine("✅ Đã seed dữ liệu mẫu thành công!");
}