﻿﻿﻿/*using EasyBuy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
namespace EasyBuy.Controllers
{
    public class HomeController : Controller
    {
        private readonly EasyBuyContext _context;
        private EasyBuy.Method.Method method = new EasyBuy.Method.Method();
        public HomeController(EasyBuyContext context)
        {
            _context = context;
        }
        public IActionResult TrangChu(string? search, int? cate, int? brandId, decimal? minPrice, decimal? maxPrice)
        {
            try
            {
                ViewBag.Categories = _context.Categories.ToList();
                ViewBag.Brands = _context.Brands.ToList();
                var products = _context.Products
                    .Where(p => p.StatusProduct != "hidden" && p.Quantity > 0)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    products = products.Where(p => p.ProductName.Contains(search));
                }

                if (cate.HasValue)
                {
                    products = products.Where(p => p.CateId == cate.Value);
                }

                if (brandId.HasValue)
                {
                    products = products.Where(p => p.BrandId == brandId.Value); 
                }

                if (minPrice.HasValue)
                {
                    products = products.Where(p => p.SellingPrice >= minPrice.Value);
                }

                if (maxPrice.HasValue)
                {
                    products = products.Where(p => p.SellingPrice <= maxPrice.Value);
                }

                return View(products.ToList());
            }
            catch (Exception)
            {
                ViewBag.Error = "Có lỗi hệ thống. Vui lòng thử lại sau.";
                return View(new List<Product>());
            }
        }


        [HttpGet]
       public async Task<IActionResult> ViewProductDetails(int productId)
       {
           try
           {
               var userId = HttpContext.Session.GetInt32("UserID");
    
               var detail = await _context.Products
                   .Include(p => p.Ratings.Where(r => r.IsApproved == true))
                   .ThenInclude(r => r.User)
                   .FirstOrDefaultAsync(p => p.ProductId == productId);
    
               if (detail == null)
               {
                   ViewBag.Error = "Không tìm thấy sản phẩm";
                   return RedirectToAction("Error", "NotFoundPage");
               }
               bool existingRating = false;
               bool hasPurchased = false;
               if (userId != null)
               {
                   hasPurchased = await _context.Orders
                       .AnyAsync(o => o.UserId == userId
                                   && o.Status == "Đã giao"
                                   && o.OrderDetails.Any(od => od.ProductId == productId));
                   existingRating = await _context.Ratings
                       .AnyAsync(r => r.ProductId == productId && r.UserId == userId);
               }
               ViewBag.HasPurchased = hasPurchased;
               ViewBag.ExistingRating = existingRating;
    
               detail.ViewCount = (detail.ViewCount ?? 0) + 1;
               await _context.SaveChangesAsync();
    
               return View(detail);
           }
           catch (Exception ex)
           {
               Console.WriteLine(ex);
               ViewBag.Error = "Có lỗi hệ thống. Vui lòng thử lại sau";
               return RedirectToAction("Error", "NotFoundPage");
           }
       }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
}*/

using EasyBuy.Models;
using EasyBuy.Services.Pricing;
using EasyBuy.Services.Repository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using EasyBuy.Library;
using EasyBuy.Services.Search;

namespace EasyBuy.Controllers
{
    public class HomeController : Controller
    {
        private readonly EasyBuyContext _context;
        private readonly MyLogger _logger;
        private readonly IProductRepository _productRepository;
        private readonly PricingService _pricingService;
        private EasyBuy.Method.Method method = new EasyBuy.Method.Method();

        // Sửa Constructor để nhận DI cả repository và pricing
        public HomeController(EasyBuyContext context, MyLogger logger, IProductRepository productRepository, PricingService pricingService)
        {
            _context = context;
            _logger = logger;
            _productRepository = productRepository;
            _pricingService = pricingService;
        }

        public async Task<IActionResult> TrangChu(string? search, int? cate, int? brandId, decimal? minPrice, decimal? maxPrice, string? categoryName, bool? isfeatured, bool? topselling, int page = 1)
        {
            try
            {
                _logger.Log("Truy cập Trang Chủ - Đang tải danh sách sản phẩm.");

                ViewBag.Categories = await _context.Categories.ToListAsync();
                ViewBag.Brands = await _context.Brands.ToListAsync();

                // Lấy danh sách sản phẩm kèm danh mục trực tiếp từ DB để đảm bảo có CategoryName
                var productsWithCate = await _context.Products
                    .Include(p => p.Cate)
                    .Where(p => p.StatusProduct != "hidden" && p.Quantity > 0)
                    .ToListAsync();

                var allProducts = (await _productRepository.GetAllAsync())
                    .Where(p => p.StatusProduct != "hidden" && p.Quantity > 0);

                if (!string.IsNullOrEmpty(search))
                {
                    _logger.Log($"Tìm kiếm sản phẩm với từ khóa: {search}");
                    allProducts = allProducts.Where(p => p.ProductName.Contains(search));
                }

                if (cate.HasValue)
                {
                    allProducts = allProducts.Where(p => p.CateId == cate.Value);
                }

                if (brandId.HasValue)
                {
                    allProducts = allProducts.Where(p => p.BrandId == brandId.Value);
                }

                if (!string.IsNullOrEmpty(categoryName))
                {
                    var catSearch = categoryName.ToLower();
                    var categoryMatchIds = productsWithCate
                        .Where(p => p.Cate != null && (
                            p.Cate.CategoryName.ToLower().Contains(catSearch) ||
                            (catSearch.Contains("dien thoai") && p.Cate.CategoryName.ToLower().Contains("điện thoại")) ||
                            (catSearch.Contains("may tinh bang") && p.Cate.CategoryName.ToLower().Contains("máy tính bảng")) ||
                            (catSearch.Contains("phu kien") && p.Cate.CategoryName.ToLower().Contains("phụ kiện"))
                        ))
                        .Select(p => p.ProductId)
                        .ToList();
                    allProducts = allProducts.Where(p => categoryMatchIds.Contains(p.ProductId));
                }

                if (isfeatured == true)
                {
                    allProducts = allProducts.Where(p => p.IsFeatured == true);
                }

                if (topselling == true)
                {
                    allProducts = allProducts.OrderByDescending(p => p.ViewCount);
                }
                else
                {
                    allProducts = allProducts.OrderByDescending(p => p.UpdatedAt);
                }

                var productList = allProducts.ToList();

                if (minPrice.HasValue)
                {
                    productList = productList.Where(p => _pricingService.CalculatePrice(p) >= minPrice.Value).ToList();
                }

                if (maxPrice.HasValue)
                {
                    productList = productList.Where(p => _pricingService.CalculatePrice(p) <= maxPrice.Value).ToList();
                }

                // Phân trang (15 sản phẩm / trang)
                int pageSize = 15;
                int totalItems = productList.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                productList = productList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;

                // Thêm dữ liệu cho các mục sản phẩm phân loại trên trang chủ
                ViewBag.FeaturedProducts = productsWithCate
                    .Where(p => p.IsFeatured == true)
                    .OrderByDescending(p => p.UpdatedAt)
                    .Take(8)
                    .ToList();

                ViewBag.TopSellingProducts = productsWithCate
                    .OrderByDescending(p => p.ViewCount)
                    .Take(8)
                    .ToList();

                ViewBag.Phones = productsWithCate
                    .Where(p => p.Cate != null && (
                        p.Cate.CategoryName.ToLower().Contains("điện thoại") || 
                        p.Cate.CategoryName.ToLower().Contains("dien thoai") ||
                        p.Cate.CategoryName.ToLower().Contains("phone") ||
                        p.Cate.CategoryName.ToLower().Contains("smartphone")
                    ))
                    .OrderByDescending(p => p.UpdatedAt)
                    .Take(8)
                    .ToList();

                ViewBag.Tablets = productsWithCate
                    .Where(p => p.Cate != null && (
                        p.Cate.CategoryName.ToLower().Contains("máy tính bảng") || 
                        p.Cate.CategoryName.ToLower().Contains("may tinh bang") || 
                        p.Cate.CategoryName.ToLower().Contains("ipad") ||
                        p.Cate.CategoryName.ToLower().Contains("tablet")
                    ))
                    .OrderByDescending(p => p.UpdatedAt)
                    .Take(8)
                    .ToList();

                ViewBag.Laptops = productsWithCate
                    .Where(p => p.Cate != null && (
                        p.Cate.CategoryName.ToLower().Contains("laptop") ||
                        p.Cate.CategoryName.ToLower().Contains("máy tính xách tay")
                    ))
                    .OrderByDescending(p => p.UpdatedAt)
                    .Take(8)
                    .ToList();

                ViewBag.Accessories = productsWithCate
                    .Where(p => p.Cate != null && (
                        p.Cate.CategoryName.ToLower().Contains("phụ kiện") || 
                        p.Cate.CategoryName.ToLower().Contains("phu kien") ||
                        p.Cate.CategoryName.ToLower().Contains("tai nghe") ||
                        p.Cate.CategoryName.ToLower().Contains("sạc") ||
                        p.Cate.CategoryName.ToLower().Contains("ốp")
                    ))
                    .OrderByDescending(p => p.UpdatedAt)
                    .Take(8)
                    .ToList();

                return View(productList);
            }
            catch (Exception ex)
            {
                // Ghi log lỗi chi tiết thay vì chỉ để trống
                _logger.Log($"LỖI TRANG CHỦ: {ex.Message}");
                ViewBag.Error = "Có lỗi hệ thống. Vui lòng thử lại sau.";
                return View(new List<Product>());
            }
        }

                [HttpGet]
        public IActionResult SearchLive(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return Json(new List<object>());
            }

            // 1. Ghi log qua Singleton Logger có sẵn
            _logger.Log($"Live Search với từ khóa: {keyword}");

            // 2. Sử dụng Singleton Cache để lấy dữ liệu. Nếu đã tìm từ này rồi thì lấy luôn không cần gọi Database
            if (SearchCacheSingleton.Instance.TryGet(keyword, out var cachedData))
            {
                return Json(cachedData);
            }

            // Tùy chỉnh theo Entity của bạn (giả sử tên là Products)
            // Lấy tối đa 5-7 sản phẩm để dropdown không bị quá dài
            var products = _context.Products
                .Where(p => p.ProductName.Contains(keyword) && p.StatusProduct != "hidden")
                .Select(p => new 
                {
                    productId = p.ProductId,
                    productName = p.ProductName,
                    price = p.SellingPrice,
                    // Nếu có ảnh, lấy ảnh đầu tiên hoặc đường dẫn ảnh mặc định
                    imageUrl = p.ImagePr ?? "/images/default-product.png" 
                })
                .Take(5) 
                .ToList();

            // 3. Lưu kết quả mới vào Singleton Cache để tái sử dụng cho các lần gõ tiếp theo
            SearchCacheSingleton.Instance.Add(keyword, products);

            return Json(products);
}


        [HttpGet]
        public async Task<IActionResult> ViewProductDetails(int productId)
        {
            try
            {
                _logger.Log($"Xem chi tiết sản phẩm ID: {productId}");

                var userId = HttpContext.Session.GetInt32("UserID");

                var detail = await _context.Products
                    .Include(p => p.Ratings!.Where(r => r.IsApproved == true))
 .ThenInclude(r => r.User)
                    .FirstOrDefaultAsync(p => p.ProductId == productId);

                if (detail == null)
                {
                    _logger.Log($"Không tìm thấy sản phẩm ID: {productId}");
                    ViewBag.Error = "Không tìm thấy sản phẩm";
                    return RedirectToAction("Error", "NotFoundPage");
                }
                bool existingRating = false;
                bool hasPurchased = false;
                if (userId != null)
                {
                    hasPurchased = await _context.Orders
                        .AnyAsync(o => o.UserId == userId
                                    && o.Status == "Đã giao"
                                    && o.OrderDetails.Any(od => od.ProductId == productId));
                    existingRating = await _context.Ratings
                        .AnyAsync(r => r.ProductId == productId && r.UserId == userId);
                }
                ViewBag.HasPurchased = hasPurchased;
                ViewBag.ExistingRating = existingRating;

                detail.ViewCount = (detail.ViewCount ?? 0) + 1;
                await _context.SaveChangesAsync();

                return View(detail);
            }
            catch (Exception ex)
            {
                // Log lại lỗi chi tiết
                _logger.Log($"LỖI CHI TIẾT SẢN PHẨM (ID {productId}): {ex.Message}");
                Console.WriteLine(ex);
                ViewBag.Error = "Có lỗi hệ thống. Vui lòng thử lại sau";
                return RedirectToAction("Error", "NotFoundPage");
            }
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
}
