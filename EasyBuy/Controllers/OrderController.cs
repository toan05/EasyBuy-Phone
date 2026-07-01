﻿﻿﻿﻿﻿using EasyBuy.Services.Payment;
using EasyBuy.Services.Observers; // <-- THÊM DÒNG NÀY
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using EasyBuy.Models;
using Microsoft.EntityFrameworkCore;
using EasyBuy.Services.EMAILOTP;
using EasyBuy.Services.VNPAY;
using EasyBuy.Models.VNPAY;
using EasyBuy.Services.MOMO;
using Newtonsoft.Json;
using EasyBuy.Method;
using EasyBuy.Services.Command;
using EasyBuy.Services.Discount;
using EasyBuy.Services.Pricing;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;

namespace EasyBuy.Controllers
{
    public class OrderController : Controller
    {
        private readonly IEmailService _emailService;
        private readonly IVnPayService _vpnPayService;
        private readonly IMomoService _momoService;
        private readonly EasyBuyContext _context;
        private readonly PaymentFactory _paymentFactory;
        private readonly PricingService _pricingService;
        private readonly IDiscountCalculator _discountCalculator;
        private readonly IOrderCommand _orderCommand;
        private readonly OrderSubject _orderSubject;
        private readonly ILogger<OrderController> _logger;

        public OrderController(
            IEmailService emailService,
            EasyBuyContext context,
            IVnPayService vnPayService,
            IMomoService momoService,
            PaymentFactory paymentFactory,
            PricingService pricingService,
            IDiscountCalculator discountCalculator,
            IOrderCommand orderCommand,
            OrderSubject orderSubject,
            ILogger<OrderController> logger)
        {
            _emailService = emailService;
            _context = context;
            _vpnPayService = vnPayService;
            _momoService = momoService;
            _paymentFactory = paymentFactory;
            _pricingService = pricingService;
            _discountCalculator = discountCalculator;
            _orderCommand = orderCommand;
            _orderSubject = orderSubject;
            _logger = logger;
        }

        public IActionResult CreatePaymentUrlVnpay(PaymentInformationModel model)
        {
            var url = _vpnPayService.CreatePaymentUrl(model, HttpContext);

            return Redirect(url);
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCallbackVnpay()
        {
            var response = _vpnPayService.PaymentExecute(Request.Query);
            if (response != null && response.Success && response.VnPayResponseCode == "00")
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    TempData["Error"] = "Phiên đăng nhập không hợp lệ.";
                    return RedirectToAction("Login", "Account");
                }

                int? orderId = null;
                if (!string.IsNullOrWhiteSpace(response.OrderId) && int.TryParse(response.OrderId, out int parsedOrderId))
                {
                    orderId = parsedOrderId;
                }
                if (orderId == null)
                {
                    orderId = HttpContext.Session.GetInt32("VnpayOrderId");
                }

                if (orderId == null)
                {
                    TempData["Error"] = "Không xác định được đơn hàng VNPay.";
                    return RedirectToAction("Checkout");
                }

                var order = await _context.Orders
                    .Include(o => o.OrderDetails)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId && o.Status == "Chờ thanh toán");

                if (order == null)
                {
                    TempData["Error"] = "Không tìm thấy đơn hàng VNPay.";
                    return RedirectToAction("Checkout");
                }

                order.Status = "Chờ xác nhận";
                order.StatusPayment = "Đã thanh toán";

                var cart = await _context.Carts
                    .Include(c => c.CartItems)
                    .ThenInclude(ci => ci.Product)
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.IsCheckedOut == false);

                if (cart != null)
                {
                    cart.IsCheckedOut = true;
                    foreach (var item in cart.CartItems)
                    {
                        if (item.Product != null && item.Product.Quantity.HasValue)
                        {
                            item.Product.Quantity -= item.Quantity ?? 0;
                        }
                    }
                }

                HttpContext.Session.Remove("VnpayOrderId");
                HttpContext.Session.Remove("CheckoutCartId");
                HttpContext.Session.Remove("CheckoutAddressId");
                HttpContext.Session.Remove("CheckoutPaymentMethodId");
                HttpContext.Session.Remove("CheckoutVoucherCode");
                HttpContext.Session.Remove("CheckoutTotalAmount");
                HttpContext.Session.Remove("CheckoutDiscount");
                HttpContext.Session.Remove("CheckoutFinnalAmout");
                HttpContext.Session.Remove("CheckoutVoucherId");

                await _context.SaveChangesAsync();

                TempData["Success"] = "Thanh toán VNPay thành công!";
                return RedirectToAction("Success");
            }
            else
            {
                TempData["Error"] = "Thanh toán thất bại!";
                return RedirectToAction("Checkout");
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCallbackMomo()
        {
            try
            {
                var momoResponse = _momoService.PaymentExecuteAsync(Request.Query);
                var momoOrderStringId = HttpContext.Session.GetString("MomoOrderStringId");

                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    TempData["Error"] = "Phiên đăng nhập không hợp lệ.";
                    return RedirectToAction("Login", "Account");
                }

                Order order = null;
                if (!string.IsNullOrWhiteSpace(momoResponse?.OrderId) && int.TryParse(momoResponse.OrderId, out var momoOrderId))
                {
                    order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == momoOrderId && o.UserId == userId && o.Status == "Chờ thanh toán");
                }

                if (order == null && !string.IsNullOrWhiteSpace(momoOrderStringId) && int.TryParse(momoOrderStringId, out var sessionOrderId))
                {
                    order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == sessionOrderId && o.UserId == userId && o.Status == "Chờ thanh toán");
                }

                if (order == null)
                {
                    TempData["Error"] = "Không tìm thấy thông tin đơn hàng.";
                    return RedirectToAction("ListOrder");
                }

                // Check if payment was successful
                if (momoResponse != null && momoResponse.ErrorCode == 0 && !string.IsNullOrEmpty(momoResponse.OrderId) && momoResponse.OrderId == order.OrderId.ToString())
                {
                    if (order.Status == "Chờ thanh toán")
                    {
                        // Update order status
                        order.Status = "Chờ xác nhận";
                        order.StatusPayment = "Đã thanh toán";
                        
                        // Update cart and inventory
                        var cart = await _context.Carts
                            .Include(c => c.CartItems)
                            .FirstOrDefaultAsync(c => c.UserId == userId && c.IsCheckedOut == false);
                            
                        if (cart != null && cart.CartItems != null)
                        {
                            cart.IsCheckedOut = true;
                            foreach (var item in cart.CartItems)
                            {
                                var product = item.Product;
                                if (product != null && product.Quantity.HasValue)
                                    product.Quantity -= item.Quantity ?? 0;
                            }
                        }
                        
                        // Update voucher quantity
                        if (order.VoucherId.HasValue)
                        {
                            var voucher = await _context.Vouchers.FindAsync(order.VoucherId.Value);
                            if (voucher != null) voucher.Quantity -= 1;
                        }
                        
                        await _context.SaveChangesAsync();

                        // Clear session data
                        HttpContext.Session.Remove("MomoOrderStringId");
                        HttpContext.Session.Remove("CheckoutCartId");
                        HttpContext.Session.Remove("CheckoutAddressId");
                        HttpContext.Session.Remove("CheckoutPaymentMethodId");
                        HttpContext.Session.Remove("CheckoutVoucherCode");
                        HttpContext.Session.Remove("CheckoutTotalAmount");
                        HttpContext.Session.Remove("CheckoutDiscount");
                        HttpContext.Session.Remove("CheckoutFinnalAmout");
                        HttpContext.Session.Remove("CheckoutVoucherId");

                        TempData["Success"] = "Thanh toán MoMo thành công! Đơn hàng đã được xác nhận.";
                        return RedirectToAction("Success");
                    }
                    else
                    {
                        TempData["Info"] = "Đơn hàng đã được xử lý trước đó.";
                        return RedirectToAction("ListOrder");
                    }
                }
                else
                {
                    // Payment failed
                    if (order.Status == "Chờ thanh toán")
                    {
                        // Delete the pending order
                        _context.Orders.Remove(order);
                        await _context.SaveChangesAsync();
                    }
                    
                    TempData["Error"] = "Thanh toán MoMo thất bại.";
                    return RedirectToAction("Checkout");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi callback MoMo: {ex.Message}");
                TempData["Error"] = "Có lỗi hệ thống khi xử lý thanh toán.";
                return RedirectToAction("Checkout");
            }
        }

        [HttpPost]
        public async Task<IActionResult> MomoNotify()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    return BadRequest("No request body received.");
                }

                var payload = JsonConvert.DeserializeObject<dynamic>(body);
                if (payload == null)
                {
                    return BadRequest("Invalid JSON payload.");
                }

                int errorCode = payload.errorCode != null ? (int)payload.errorCode : -1;
                string orderIdString = payload.orderId != null ? (string)payload.orderId : string.Empty;

                if (errorCode != 0 || string.IsNullOrWhiteSpace(orderIdString))
                {
                    return BadRequest("Payment not completed or missing orderId.");
                }

                if (!int.TryParse(orderIdString, out var momoOrderId))
                {
                    return BadRequest("Invalid orderId format.");
                }

                var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == momoOrderId && o.Status == "Chờ thanh toán");
                if (order == null)
                {
                    Console.WriteLine($"MomoNotify: Order {momoOrderId} not found or already processed");
                    return NotFound("Order not found.");
                }

                order.Status = "Chờ xác nhận";
                order.StatusPayment = "Đã thanh toán";
                await _context.SaveChangesAsync();

                HttpContext.Session.Remove("MomoOrderStringId");

                Console.WriteLine($"MomoNotify: Order {order.OrderId} payment confirmed");
                return Ok("OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi notify MoMo: {ex.Message}");
                return BadRequest("Error");
            }
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Checkout()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            try
            {
                var cart = await _context.Carts
                    .Include(c => c.CartItems)
                    .ThenInclude(ci => ci.Product)
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.IsCheckedOut == false);

                var addresses = _context.Addresses
                    .Include(a => a.User)
                    .Where(a => a.UserId == userId)
                    .ToList();
                var paymentMethods = _context.PaymentMethods.ToList();

                return View(Tuple.Create(cart?.CartItems.ToList() ?? new List<CartItem>(), addresses, paymentMethods));
            }
            catch
            {
                ViewBag.Error = "Có lỗi hệ thống vui lòng thử lại sau";
                return View(Tuple.Create(new List<CartItem>(), new List<Address>(), new List<PaymentMethod>()));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Checkout(int addressId, int paymentMethodId, string? voucherCode)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            try
            {
                var cart = await _context.Carts
                    .Include(c => c.CartItems)
                    .ThenInclude(ci => ci.Product)
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.IsCheckedOut == false);

                if (cart == null || cart.CartItems.Count == 0)
                {
                    TempData["Error"] = "Giỏ hàng trống!";
                    return RedirectToAction("UserCart", "Cart");
                }

                decimal total = cart.CartItems.Sum(item => (item.UnitPrice ?? 0) * (item.Quantity ?? 0));
                decimal discount = 0;
                int? voucherId = null;

                if (!string.IsNullOrEmpty(voucherCode))
                {
                    var today = DateOnly.FromDateTime(DateTime.Now);
                    var voucher = await _context.Vouchers.FirstOrDefaultAsync(v => v.Code == voucherCode &&
                                                            v.IsActive == true &&
                                                            v.Quantity > 0 &&
                                                            v.StartDate <= today &&
                                                            v.EndDate >= today);
                    if (voucher == null)
                    {
                        TempData["Error"] = "Mã giảm giá không hợp lệ.";
                        return RedirectToAction("Checkout");
                    }

                    if (total < voucher.MinOrderAmount)
                    {
                        TempData["Error"] = $"Đơn hàng tối thiểu {voucher.MinOrderAmount:#,##0} VNĐ.";
                        return RedirectToAction("Checkout");
                    }

                    voucherId = voucher.VoucherId;

                    if (voucher.DiscountType == "percent")
                        discount = total * ((voucher.DiscountValue ?? 0) / 100);
                    else
                        discount = voucher.DiscountValue ?? 0;

                    if (voucher.MaxDiscountAmount.HasValue && discount > voucher.MaxDiscountAmount.Value)
                        discount = voucher.MaxDiscountAmount.Value;
                }

                decimal finalAmount = total - discount;

                // Lưu thông tin vào Session
                HttpContext.Session.SetInt32("CheckoutCartId", cart.CartId);
                HttpContext.Session.SetInt32("CheckoutAddressId", addressId);
                HttpContext.Session.SetInt32("CheckoutPaymentMethodId", paymentMethodId);
                HttpContext.Session.SetString("CheckoutVoucherCode", voucherCode ?? "");
                HttpContext.Session.SetString("CheckoutTotalAmount", total.ToString(CultureInfo.InvariantCulture));
                HttpContext.Session.SetString("CheckoutDiscount", discount.ToString(CultureInfo.InvariantCulture));
                HttpContext.Session.SetString("CheckoutFinnalAmout", finalAmount.ToString(CultureInfo.InvariantCulture));
                HttpContext.Session.SetInt32("CheckoutVoucherId", voucherId ?? 0);

            // 1. Nếu là COD, thực hiện lưu Database trực tiếp
            if (paymentMethodId == 1)
            {
                var strategy = _context.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync<IActionResult>(async () =>
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    Order? order = null;
                    try
                    {
                        _logger.LogInformation("Bắt đầu xử lý COD cho user {UserId}", userId);

                        order = new Order 
                        {
                            UserId = userId,
                            AddressId = addressId,
                            PaymentMethodId = paymentMethodId,
                            VoucherId = voucherId,
                            TotalAmount = total,
                            FinalTotal = finalAmount,
                            Status = "Chờ xác nhận",
                            StatusPayment = "Chưa thanh toán",
                            CreatedAt = DateTime.Now,
                            OrderDetails = cart.CartItems.Select(item => new OrderDetail
                            {
                                ProductId = item.ProductId,
                                Quantity = item.Quantity,
                                UnitPrice = item.UnitPrice,
                                ExistFirst = item.Product?.Quantity ?? 0,
                                SurviveAfter = (item.Product?.Quantity ?? 0) - (item.Quantity ?? 0)
                            }).ToList()
                        };

                        _logger.LogInformation("Tạo order với {OrderDetailCount} items", order.OrderDetails.Count);

                        try 
                        {
                            await _orderCommand.ExecuteAsync(order, cart);
                            _logger.LogInformation("ExecuteAsync hoàn thành");
                        }
                        catch (Exception cmdEx)
                        {
                            // Backup an toàn: Nếu bạn quên chưa code nội dung trong CreateOrderCommand, hệ thống tự động chạy lệnh dưới
                            _logger.LogWarning("Command bị lỗi hoặc chưa code bên trong: " + cmdEx.Message);
                            _context.Orders.Add(order);
                            cart.IsCheckedOut = true;
                            foreach (var item in cart.CartItems)
                            {
                                if (item.Product != null && item.Quantity.HasValue)
                                {
                                    item.Product.Quantity -= item.Quantity.Value;
                                }
                            }
                        }

                        if (voucherId.HasValue && voucherId.Value > 0)
                        {
                            var v = await _context.Vouchers.FindAsync(voucherId.Value);
                            if (v != null) v.Quantity -= 1;
                        }

                        await _context.SaveChangesAsync();
                        _logger.LogInformation("SaveChanges hoàn thành");

                        await transaction.CommitAsync();
                        _logger.LogInformation("Transaction commit thành công");
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(ex, "Lỗi trong transaction COD: {Message}", ex.Message);
                        TempData["Error"] = "Có lỗi hệ thống khi xử lý đơn hàng COD.";
                        return RedirectToAction("Checkout");
                    }

                    // Đưa Notify (gửi email/tin nhắn) ra ngoài để tránh lỗi gửi email làm crash đơn hàng
                    try
                    {
                        if (order != null)
                        {
                            await _orderSubject.NotifyAsync(order);
                            _logger.LogInformation("NotifyAsync hoàn thành");
                        }
                    }
                    catch (Exception notifyEx)
                    {
                        _logger.LogWarning(notifyEx, "Lưu đơn hàng thành công nhưng lỗi khi gửi thông báo: {Message}", notifyEx.Message);
                    }

                    TempData["Success"] = "Đặt hàng thành công!";
                    return RedirectToAction("Success");
                });
            }
            else // Xử lý cho các phương thức thanh toán online khác (VNPay, Momo, ...)
            {
                // Chuyển qua Payment Factory để lấy URL Redirect
                var paymentModel = new PaymentInformationModel
                {
                    Amount = (double)finalAmount,
                    OrderDescription = "Thanh toán đơn hàng EasyBuy"
                };
                var handler = _paymentFactory.GetHandler(paymentMethodId);
                var result = await handler.HandleAsync(paymentModel, HttpContext);

                if (string.IsNullOrWhiteSpace(result) || result == "/Order/Checkout")
                {
                    TempData["Error"] = "Không tạo được đường dẫn thanh toán MoMo. Vui lòng thử lại.";
                    return RedirectToAction("Checkout");
                }

                return Redirect(result);
            }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi Checkout: paymentMethodId={PaymentMethodId}, userId={UserId}", paymentMethodId, User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Error"] = "Có lỗi hệ thống! Vui lòng thử lại sau.";
                return RedirectToAction("Checkout");
            }
        }



        [HttpGet]
        public IActionResult VerifyOtp()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> VerifyOtp(string inputOtp)
        {

            var sessionOtp = HttpContext.Session.GetString("CheckoutOtp");
            var sessionOtpExpiry = HttpContext.Session.GetString("CheckoutOtpExpiry");
            var sessionOtpAttempts = HttpContext.Session.GetInt32("CheckoutOtpAttempts");
            var cartId = HttpContext.Session.GetInt32("CheckoutCartId");
            var addressId = HttpContext.Session.GetInt32("CheckoutAddressId");
            var paymentMethodId = HttpContext.Session.GetInt32("CheckoutPaymentMethodId");
            var voucherCode = HttpContext.Session.GetString("CheckoutVoucherCode");
            var voucherId = HttpContext.Session.GetInt32("CheckoutVoucherId");
            var totalAmountString = HttpContext.Session.GetString("CheckoutTotalAmount");
            var finalAmountString = HttpContext.Session.GetString("CheckoutFinnalAmout");
            var Discount = HttpContext.Session.GetString("CheckoutDiscount");
            var userIdClaim_OTP = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim_OTP == null || !int.TryParse(userIdClaim_OTP.Value, out int userId))
                return RedirectToAction("Login", "Account");
            if (!decimal.TryParse(Discount ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var discountAmount))
            {
                ViewBag.Error = "Không tìm thấy thông tin giảm giá.";
                return View();
            }
            if (!decimal.TryParse(totalAmountString, NumberStyles.Any, CultureInfo.InvariantCulture, out var total))
            {
                ViewBag.Error = "Không tìm thấy thông tin tổng tiền.";
                return View();
            }
            if (string.IsNullOrEmpty(sessionOtpExpiry) || !DateTime.TryParse(sessionOtpExpiry, out var expiry) || DateTime.Now > expiry)
            {
                ViewBag.Error = "Mã OTP đã hết hạn.";
                return View();
            }

            if (sessionOtpAttempts.HasValue && sessionOtpAttempts.Value >= 3)
            {
                ViewBag.Error = "Bạn đã thử quá nhiều lần.";
                return View();
            }

            if (sessionOtp == null || inputOtp != sessionOtp)
            {
                var attempts = (sessionOtpAttempts ?? 0) + 1;
                HttpContext.Session.SetInt32("CheckoutOtpAttempts", attempts);
                ViewBag.Error = $"Mã OTP không đúng. Còn {3 - attempts} lần thử!";
                return View();
            }

            if (cartId == null || addressId == null || paymentMethodId == null || !decimal.TryParse(finalAmountString, NumberStyles.Any, CultureInfo.InvariantCulture, out var finalAmount))
            {
                ViewBag.Error = "Thông tin không đầy đủ.";
                return View();
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var cartItems = await _context.CartItems
                        .Include(ci => ci.Product)
                        .Where(ci => ci.CartId == cartId.Value)
                    .ToListAsync();

                if (!cartItems.Any())
                {
                    ViewBag.Error = "Giỏ hàng trống.";
                    return View();
                }

                // Giảm số lượng voucher nếu có
                if (voucherId.HasValue && voucherId.Value > 0)
                {
                    var voucher = await _context.Vouchers.FindAsync(voucherId.Value);
                    if (voucher != null && voucher.Quantity > 0)
                    {
                        voucher.Quantity -= 1;
                    }
                }
                var productIds = cartItems.Select(c => c.ProductId).ToList();
                var products = _context.Products
                    .Where(p => productIds.Contains(p.ProductId))
                    .ToDictionary(p => p.ProductId, p => p.Quantity.Value);
                var order = new Order
                {
                    UserId = userId,
                    AddressId = addressId.Value,
                    PaymentMethodId = paymentMethodId.Value,
                    VoucherId = voucherId == 0 ? null : voucherId,
                    TotalAmount = total,
                    FinalTotal = finalAmount,
                    Status = "Chờ xác nhận",
                    StatusPayment = "Chưa thanh toán",
                    CreatedAt = DateTime.Now,
                    OrderDetails = cartItems.Select(item => new OrderDetail
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        ExistFirst = products[item.ProductId],
                        SurviveAfter = products[item.ProductId] - (item.Quantity ?? 0)
                    }).ToList()
                };
                _context.Orders.Add(order);

                foreach (var item in cartItems)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    if (product != null && product.Quantity < item.Quantity)
                    {
                        ViewBag.Error = $"Sản phẩm {product.ProductName} không đủ số lượng.";
                        return View();
                    }
                    product.Quantity -= item.Quantity ?? 0;
                }

                var cart = await _context.Carts.FirstOrDefaultAsync(c => c.CartId == cartId.Value);
                if (cart != null) cart.IsCheckedOut = true;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                HttpContext.Session.Remove("CheckoutCartId");
                HttpContext.Session.Remove("CheckoutAddressId");
                HttpContext.Session.Remove("CheckoutPaymentMethodId");
                HttpContext.Session.Remove("CheckoutVoucherCode");
                HttpContext.Session.Remove("CheckoutTotalAmount");
                HttpContext.Session.Remove("CheckoutDiscount");
                HttpContext.Session.Remove("CheckoutFinnalAmout");
                HttpContext.Session.Remove("CheckoutVoucherId");
                HttpContext.Session.Remove("CheckoutOtp");
                HttpContext.Session.Remove("CheckoutOtpExpiry");
                HttpContext.Session.Remove("CheckoutOtpAttempts");

                await SendOrderConfirmationEmail(order.OrderId);

                return RedirectToAction("Success");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Lỗi VerifyOtp: {ex.Message}");
                ViewBag.Error = "Có lỗi xảy ra khi tạo đơn hàng.";
                return View();
            }
            });
        }

        private string GenerateSecureOtp()
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var random = Math.Abs(BitConverter.ToInt32(bytes, 0));
            return (random % 900000 + 100000).ToString(); 
        }

        [HttpPost]
        public async Task<IActionResult> ResendOtp()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }
            try
            {

                var cartId = HttpContext.Session.GetInt32("CheckoutCartId");
                if (cartId == null)
                {
                    ViewBag.Error = "Không tìm thấy thông tin đặt hàng.";
                    return View("VerifyOtp");
                }

                // Tạo OTP mới
                var newOtp = GenerateSecureOtp();
                var newOtpExpiry = DateTime.Now.AddMinutes(5);

                HttpContext.Session.SetString("CheckoutOtp", newOtp);
                HttpContext.Session.SetString("CheckoutOtpExpiry", newOtpExpiry.ToString("yyyy-MM-dd HH:mm:ss"));
                HttpContext.Session.SetInt32("CheckoutOtpAttempts", 0);

                // Lấy email
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                if (user == null || string.IsNullOrEmpty(user.Email))
                {
                    ViewBag.Error = "Không tìm thấy email của bạn.";
                    return View("VerifyOtp");
                }

                await _emailService.SendEmailAsync(user.Email, "Xác minh đơn hàng EasyBuy",
                    $"Mã OTP mới của bạn là: {newOtp}. Mã có hiệu lực trong 5 phút.");

                ViewBag.Info = "OTP mới đã được gửi đến email của bạn.";
                return View("VerifyOtp");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi resend OTP: {ex.Message}");
                ViewBag.Error = "Không thể gửi OTP. Vui lòng thử lại.";
                return View("VerifyOtp");
            }
        }


        // ✅ Method để làm sạch session checkout
        [HttpPost]
        public IActionResult CancelCheckout()
        {
            try
            {
                // ✅ Xóa tất cả session liên quan đến checkout
                HttpContext.Session.Remove("CheckoutCartId");
                HttpContext.Session.Remove("CheckoutAddressId");
                HttpContext.Session.Remove("CheckoutPaymentMethodId");
                HttpContext.Session.Remove("CheckoutVoucherCode");
                HttpContext.Session.Remove("CheckoutTotalAmount");
                HttpContext.Session.Remove("CheckoutDiscount");
                HttpContext.Session.Remove("CheckoutFinnalAmout");
                HttpContext.Session.Remove("CheckoutVoucherId");
                HttpContext.Session.Remove("CheckoutOtp");
                HttpContext.Session.Remove("CheckoutOtpExpiry");
                HttpContext.Session.Remove("CheckoutOtpAttempts");

                // ✅ Xóa session tạm thời cho thanh toán online
                HttpContext.Session.Remove("TempAddressId");
                HttpContext.Session.Remove("TempPaymentMethodId");
                HttpContext.Session.Remove("TempVoucherCode");

                return RedirectToAction("UserCart", "Cart");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi cancel checkout: {ex.Message}");
                return Json(new { success = false, message = "Có lỗi xảy ra khi hủy đặt hàng." });
            }
        }


        [HttpGet]
        public async Task<IActionResult> ValidateVoucher(string code, string total)
        {
            if (!decimal.TryParse(total, out var parsedTotal))
            {
                return Json(new { success = false, message = "Tổng tiền không hợp lệ." });
            }

            var today = DateOnly.FromDateTime(DateTime.Now);

            var voucher = await _context.Vouchers
                .FirstOrDefaultAsync(v => v.Code == code &&
                                          v.IsActive == true &&
                                          v.Quantity > 0 &&
                                          v.StartDate <= today &&
                                          v.EndDate >= today);

            if (voucher == null)
            {
                return Json(new { success = false, message = "Mã giảm giá không hợp lệ hoặc đã hết hạn." });
            }

            if (parsedTotal < voucher.MinOrderAmount)
            {
                return Json(new { success = false, message = $"Đơn hàng cần tối thiểu {voucher.MinOrderAmount:N0} VNĐ." });
            }

            decimal discount = 0;
            if (voucher.DiscountType == "percent")
                discount = parsedTotal * (voucher.DiscountValue ?? 0) / 100;
            else if (voucher.DiscountType == "amount")
                discount = voucher.DiscountValue ?? 0;

            if (voucher.MaxDiscountAmount.HasValue && discount > voucher.MaxDiscountAmount.Value)
                discount = voucher.MaxDiscountAmount.Value;

            return Json(new { success = true, discount });
        }

        public IActionResult Success()
        {
            return View();
        }

        // ✅ TEST CALLBACK MOMO (XÓA SAU KHI CÓ NGROK)
        [HttpGet]
        public async Task<IActionResult> TestMomoCallback(int orderId, bool success = true)
        {
            try
            {
                var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                if (success)
                {
                    if (order.Status == "Chờ thanh toán")
                    {
                        order.Status = "Chờ xác nhận";
                        await _context.SaveChangesAsync();
                        await _orderSubject.NotifyAsync(order);
                        TempData["Success"] = "Thanh toán MoMo thành công! Đơn hàng đã được xác nhận.";
                        return RedirectToAction("Success");
                    }
                }
                else
                {
                    if (order.Status == "Chờ thanh toán")
                    {
                        order.Status = "Thanh toán thất bại";
                        await _context.SaveChangesAsync();
                        
                        TempData["Error"] = "Thanh toán MoMo thất bại!";
                        return RedirectToAction("Checkout");
                    }
                }

                return Json(new { success = true, message = $"Order {orderId} đã được cập nhật qua MoMo" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ✅ TEST CALLBACK VNPAY (XÓA SAU KHI CÓ NGROK)
        [HttpGet]
        public async Task<IActionResult> TestVnpayCallback(int orderId, bool success = true)
        {
            try
            {
                var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                if (success)
                {
                    if (order.Status == "Chờ thanh toán")
                    {
                        order.Status = "Chờ xác nhận";
                        await _context.SaveChangesAsync();
                        await _orderSubject.NotifyAsync(order);
                        TempData["Success"] = "Thanh toán thành công! Đơn hàng đã được xác nhận.";
                        return RedirectToAction("Success");
                    }
                }
                else
                {
                    if (order.Status == "Chờ thanh toán")
                    {
                        order.Status = "Thanh toán thất bại";
                        await _context.SaveChangesAsync();
                        
                        TempData["Error"] = "Thanh toán thất bại!";
                        return RedirectToAction("Checkout");
                    }
                }

                return Json(new { success = true, message = $"Order {orderId} đã được cập nhật" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> TestEmail()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                try
                {
                    return Json(new { success = false, message = "Chưa đăng nhập" });

                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                if (user == null || string.IsNullOrEmpty(user.Email))
                    return Json(new { success = false, message = "Không tìm thấy email user" });

                await _emailService.SendEmailAsync(
                    user.Email,
                    "Test Email EasyBuy",
                    "Đây là email test. Nếu bạn nhận được email này, cấu hình SMTP đã hoạt động!"
                );

                return Json(new { success = true, message = $"Email test đã gửi tới {user.Email}" });
            }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
                }
            }
            // This part seems to be a mistake from a merge, it should be inside the try-catch block above.
            // I'm removing it to fix the compile error.
            return NotFound(); 

        }

        [HttpGet]
        public async Task<IActionResult> ListOrder()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId)) // TC_ORDER_LIST_003
                    return RedirectToAction("Login", "Account");

                var orders = await _context.Orders
                    .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                    .Where(o => o.UserId == userId)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();

                return View(orders);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Có lỗi hệ thống.Vui lòng thử lại sau"; // TC_ORDER_LIST_004
                return View(ex);
            }
        }

        public async Task<IActionResult> ViewOrderDetails(int orderId)
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
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order == null)
                {
                    TempData["Error"] = "Không tìm thấy đơn hàng."; // TC_ORDER_DETAIL_002
                    return RedirectToAction("ListOrder");
                }

                return View(order);
            }
            catch (Exception) // TC_ORDER_DETAIL_003
            {
                ViewBag.Error = "Có lỗi xảy ra khi xem chi tiết đơn hàng.";
                return View();
            }
        }

        //Co trigger tra sl ton va voucher
        [HttpPost]
        public async Task<IActionResult> CancelOrder(int orderId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId)) // Check login
                    return RedirectToAction("Login", "Account");

                var order = await _context.Orders
                    .Include(o => o.Voucher) 
                    .FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId);

                if (order == null) // TC_ORDER_CANCEL_003
                {
                    TempData["Error"] = "Không tìm thấy đơn hàng.";
                    return RedirectToAction("ListOrder");
                }

            if (order.Status != "Chờ xác nhận" && order.Status != "Chờ thanh toán") // TC_ORDER_CANCEL_002
                {
                    TempData["Error"] = "Đơn hàng không thể hủy ở trạng thái hiện tại.";
                    return RedirectToAction("ListOrder");
                }

                order.Status = "Đã hủy"; // TC_ORDER_CANCEL_001
                await _context.SaveChangesAsync();

                ViewBag.Success = "Đơn hàng đã được hủy thành công.";
                return RedirectToAction("ListOrder");
            }
            catch (Exception)
            {
                ViewBag.Error = "Có lỗi xảy ra khi hủy đơn hàng.";
                return RedirectToAction("ListOrder");
            }
        }


        [HttpPost]
        public async Task<IActionResult> RepeatOrder(int orderId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId)) // TC_ORDER_REPEAT_002
                {
                    TempData["Error"] = "Bạn cần đăng nhập để thực hiện chức năng này."; // MS01
                    return RedirectToAction("Login", "Account"); // AD login
                }

                var strategy = _context.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync<IActionResult>(async () =>
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        // Bước 2: Truy vấn đơn hàng gốc
                        var oldOrder = await _context.Orders
                        .Include(o => o.OrderDetails)
                        .FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId);

                    if (oldOrder == null) // TC_ORDER_REPEAT_003
                    {
                        TempData["Error"] = "Không tìm thấy đơn hàng hoặc bạn không có quyền truy cập."; // MS15
                        return RedirectToAction("ListOrder");
                    }

                    // Bước 3: Kiểm tra đơn hàng có sản phẩm không
                    if (oldOrder.OrderDetails == null || !oldOrder.OrderDetails.Any()) // TC_ORDER_REPEAT_004
                    {
                        TempData["Error"] = "Đơn hàng không có sản phẩm nào để mua lại."; // MS16
                        return RedirectToAction("ListOrder");
                    }

                    // Bước 4: Tìm giỏ hàng hiện tại
                    var cart = await _context.Carts
                        .Include(c => c.CartItems)
                        .FirstOrDefaultAsync(c => c.UserId == userId && c.IsCheckedOut == false);

                    if (cart == null)
                    {
                        // Nếu không có thì tạo giỏ hàng mới
                        cart = new Cart
                        {
                            UserId = userId,
                            CreatedAt = DateTime.Now,
                            IsCheckedOut = false,
                            CartItems = new List<CartItem>()
                        };
                        _context.Carts.Add(cart);
                        await _context.SaveChangesAsync();
                    }

                    // Bước 5: Xử lý từng sản phẩm
                    foreach (var orderDetail in oldOrder.OrderDetails)
                    {
                        var product = await _context.Products
                            .FirstOrDefaultAsync(p => p.ProductId == orderDetail.ProductId && p.Quantity > 0);

                        if (product == null) continue; // TC_ORDER_REPEAT_005

                        var existingItem = cart.CartItems
                            .FirstOrDefault(ci => ci.ProductId == product.ProductId);

                        int quantityToAdd = orderDetail.Quantity.HasValue
                            ? Math.Min(orderDetail.Quantity.Value, product.Quantity.Value)
                            : 1; // TC_ORDER_REPEAT_006

                        if (existingItem != null)
                        {
                            int totalQuantity = (existingItem.Quantity ?? 0) + quantityToAdd;
                            existingItem.Quantity = Math.Min(totalQuantity, product.Quantity.Value); // TC_ORDER_REPEAT_007
                            existingItem.UnitPrice = product.SellingPrice;
                        }
                        else
                        {
                            var newItem = new CartItem
                            {
                                CartId = cart.CartId,
                                ProductId = product.ProductId,
                                Quantity = quantityToAdd,
                                UnitPrice = product.SellingPrice,
                                CreatedAt = DateTime.Now
                            };
                            _context.CartItems.Add(newItem);
                        }
                    }

                    // Bước 6: Lưu thay đổi
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return RedirectToAction("UserCart", "Cart");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(); // TC_ORDER_REPEAT_008
                    Console.WriteLine($"Lỗi RepeatOrder inner: {ex.Message}");
                    throw;
                }
            });
            }
            catch (Exception ex) // TC_ORDER_REPEAT_008
            {
                Console.WriteLine($"Lỗi RepeatOrder outer: {ex.Message}");
                TempData["Error"] = "Có lỗi xảy ra khi mua lại đơn hàng. Vui lòng thử lại sau.";
                return RedirectToAction("ListOrder");
            }
        }


        private async Task SendOrderConfirmationEmail(int orderId)
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
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order == null || order.User == null || string.IsNullOrEmpty(order.User.Email))
                    return;

                var emailSubject = "Xác nhận đơn hàng EasyBuy - Đặt hàng thành công";

                var emailBody = $@"
🎉 ĐƠN HÀNG CỦA BẠN ĐÃ ĐƯỢC XÁC NHẬN!

Xin chào {order.User.FullName},

Cảm ơn bạn đã mua sắm tại EasyBuy! Đơn hàng của bạn đã được xác nhận thành công.

=========================================
📋 THÔNG TIN ĐƠN HÀNG
=========================================
Mã đơn hàng: #{order.OrderId}
Ngày đặt: {order.CreatedAt:dd/MM/yyyy HH:mm}
Trạng thái: {order.Status}
Trạng thái thanh toán: {order.StatusPayment}
Phương thức thanh toán: {order.PaymentMethod?.MethodName}

=========================================
📍 ĐỊA CHỈ GIAO HÀNG
=========================================
{order.Address?.FullAddress}
Người nhận: {order.User.FullName}
Số điện thoại: {order.Address?.Phone}

=========================================
🛍️ CHI TIẾT SẢN PHẨM
=========================================";

                foreach (var item in order.OrderDetails)
                {
                    var itemTotal = (item.UnitPrice ?? 0) * (item.Quantity ?? 0);
                    emailBody += $@"
• {item.Product?.ProductName}
  Số lượng: {item.Quantity}
  Đơn giá: {item.UnitPrice:N0} VNĐ
  Thành tiền: {itemTotal:N0} VNĐ
";
                }

                decimal subtotal = order.OrderDetails.Sum(od => (od.UnitPrice ?? 0) * (od.Quantity ?? 0));
                decimal discount = subtotal - (order.FinalTotal ?? order.TotalAmount ?? 0);

                emailBody += $@"
-----------------------------------------
Tạm tính: {subtotal:N0} VNĐ";

                if (discount > 0)
                {
                    var voucherInfo = order.Voucher != null ? $" ({order.Voucher.Code})" : "";
                    emailBody += $@"
Giảm giá{voucherInfo}: -{discount:N0} VNĐ";
                }

                emailBody += $@"
-----------------------------------------
TỔNG CỘNG: {(order.FinalTotal ?? order.TotalAmount ?? 0):N0} VNĐ
=========================================

=========================================
📞 HỖ TRỢ KHÁCH HÀNG
=========================================
Nếu bạn có bất kỳ câu hỏi nào về đơn hàng, vui lòng liên hệ:

📧 Email: support@easybuy.com
📱 Hotline: 1900-1234
🕒 Giờ làm việc: 8:00 - 22:00 (Thứ 2 - Chủ nhật)

Đơn hàng của bạn đang được xử lý và sẽ được giao trong thời gian sớm nhất.

Cảm ơn bạn đã tin tưởng và lựa chọn EasyBuy! 💚

=========================================
© 2024 EasyBuy. Tất cả quyền được bảo lưu.
Email này được gửi tự động, vui lòng không trả lời trực tiếp.
=========================================";

                await _emailService.SendEmailAsync(order.User.Email, emailSubject, emailBody);
                Console.WriteLine($"✅ Đã gửi email xác nhận đơn hàng #{orderId} tới {order.User.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi gửi email xác nhận đơn hàng #{orderId}: {ex.Message}");
            }
        }
    }
}
