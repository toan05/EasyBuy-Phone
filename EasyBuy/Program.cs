﻿﻿﻿using EasyBuy.Models;
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
using Microsoft.AspNetCore.Mvc.ApplicationParts;
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

builder.Services.AddScoped<EasyBuy.Services.AUTH.IAuthService, EasyBuy.Services.AUTH.AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IVnPayService, VnPayService>();
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

builder.Services.AddAuthentication(options =>
    {
        // Scheme mặc định cho khách hàng (Customer). Các Area Admin/NVKD/NVKho/NVKT/NVMKT
        // luôn chỉ định rõ scheme "AdminScheme" khi xác thực nên không bị ảnh hưởng.
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options => {
        options.Cookie.Name = ".EasyBuy.Customer";
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
    })
    .AddCookie("AdminScheme", options =>
    {
        options.Cookie.Name = ".EasyBuy.Admin";
        options.LoginPath = "/Admin/Account/Login";
        options.LogoutPath = "/Admin/Account/Logout";
        options.AccessDeniedPath = "/Admin/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
    })
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options => {
        options.ClientId = builder.Configuration.GetSection("GoogleKeys:ClientId").Value ?? string.Empty;
        options.ClientSecret = builder.Configuration.GetSection("GoogleKeys:ClientSecret").Value ?? string.Empty;
        options.CallbackPath = "/signin-google";
        // Chỉ định scheme để lưu cookie sau khi đăng nhập Google thành công
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    });



var app = builder.Build();

// ==========================================================
// SEED DATA (THÊM DỮ LIỆU MẪU NẾU DATABASE TRỐNG)
// ==========================================================
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<EasyBuyContext>();
    try
    {
        // Tự động thêm các cột mới vào DB nếu chưa có (tránh lỗi database do chưa chạy Migration)
        context.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Orders]') AND name = 'FinalTotal')
            BEGIN
                ALTER TABLE [dbo].[Orders] ADD [FinalTotal] DECIMAL(10, 2) NULL;
            END
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[OrderDetail]') AND name = 'ExistFirst')
            BEGIN
                ALTER TABLE [dbo].[OrderDetail] ADD [ExistFirst] INT NULL;
            END
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[OrderDetail]') AND name = 'SurviveAfter')
            BEGIN
                ALTER TABLE [dbo].[OrderDetail] ADD [SurviveAfter] INT NULL;
            END
        ");

        SeedData(context);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Lỗi khởi tạo CSDL]: {ex.Message}");
    }
}

// ==========================================================
// 4. KÍCH HOẠT LOGGER & MIDDLEWARE (THỨ TỰ LÀ SỐNG CÒN)
// ==========================================================
var logger = app.Services.GetRequiredService<MyLogger>();
logger.Log("Hệ thống EasyBuy đã khởi động!");

// Đánh thức Singleton Cache để đọc và in lịch sử tìm kiếm ra Terminal ngay lập tức
var initSearchCache = EasyBuy.Services.Search.SearchCacheSingleton.Instance;

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
    // 1. Seed Payment Methods
    if (!context.PaymentMethods.Any())
    {
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
    }

    // 2. Seed Brands
    if (!context.Brands.Any())
    {
        context.Brands.AddRange(new[]
        {
            new Brand { NameBrand = "Apple" },
            new Brand { NameBrand = "Samsung" },
            new Brand { NameBrand = "Nike" },
            new Brand { NameBrand = "Adidas" }
        });
        context.SaveChanges();
    }

    // 3. Seed Categories
    if (!context.Categories.Any())
    {
        context.Categories.AddRange(new[]
        {
            new Category { CategoryName = "Điện thoại" },
            new Category { CategoryName = "Laptop" },
            new Category { CategoryName = "Giày dép" },
            new Category { CategoryName = "Quần áo" }
        });
        context.SaveChanges();
    }

    // 4. Seed Users
    if (!context.Users.Any(u => u.Email == "admin@easybuy.com"))
    {
        context.Users.Add(new User
        {
            FullName = "Admin",
            Email = "admin@easybuy.com",
            Password = BCrypt.Net.BCrypt.HashPassword("admin123"),
            Phone = "0123456789",
            Role = "Admin",
            AccountStatus = "Active",
            CreatedAt = DateTime.Now
        });
    }

    if (!context.Users.Any(u => u.Email == "user@easybuy.com"))
    {
        context.Users.Add(new User
        {
            FullName = "Nguyễn Văn A",
            Email = "user@easybuy.com",
            Password = BCrypt.Net.BCrypt.HashPassword("user123"),
            Phone = "0987654321",
            Role = "Customer",
            AccountStatus = "Active",
            CreatedAt = DateTime.Now
        });
    }

    context.SaveChanges();

    // 5. Seed Products
    var currentBrands = context.Brands.ToList();
    var currentCategories = context.Categories.ToList();

    var seedProducts = new[]
    {
        new { Barcode = "IPHONE15PRO001", Name = "iPhone 15 Pro", Category = "Điện thoại", Brand = "Apple", Price = 25000000m, Qty = 10, Image = "/images/iphone15.jpg" },
        new { Barcode = "SAMSUNGS24ULTRA001", Name = "Samsung Galaxy S24", Category = "Điện thoại", Brand = "Samsung", Price = 20000000m, Qty = 15, Image = "/images/galaxy-s24.jpg" },
        new { Barcode = "MACBOOKPRO16INCH001", Name = "MacBook Pro 16\"", Category = "Laptop", Brand = "Apple", Price = 50000000m, Qty = 5, Image = "/images/macbook-pro.jpg" },
        new { Barcode = "NIKEAIRMAX001", Name = "Nike Air Max", Category = "Giày dép", Brand = "Nike", Price = 3000000m, Qty = 20, Image = "/images/nike-airmax.jpg" }
    };

    foreach (var prod in seedProducts)
    {
        if (!context.Products.Any(p => p.Barcode == prod.Barcode))
        {
            var brand = currentBrands.FirstOrDefault(b => b.NameBrand == prod.Brand);
            var category = currentCategories.FirstOrDefault(c => c.CategoryName == prod.Category);
            if (brand == null || category == null)
                continue;

            context.Products.Add(new Product
            {
                ProductName = prod.Name,
                Description = prod.Name,
                SellingPrice = prod.Price,
                Quantity = prod.Qty,
                ImagePr = prod.Image,
                CateId = category.CateId,
                BrandId = brand.BrandId,
                StatusProduct = "Active",
                Barcode = prod.Barcode
            });
        }
    }

    context.SaveChanges();

    Console.WriteLine("✅ Đã seed dữ liệu mẫu thành công!");
}