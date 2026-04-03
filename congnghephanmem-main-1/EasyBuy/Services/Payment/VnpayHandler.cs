using EasyBuy.Models.VNPAY;
using EasyBuy.Services.VNPAY;

namespace EasyBuy.Services.Payment
{
    public class VnpayHandler : IPaymentHandler
    {
        private readonly IVnPayService _service;
        public VnpayHandler(IVnPayService service) => _service = service;

        public Task<string> HandleAsync(PaymentInformationModel model, HttpContext context)
        {
            return Task.FromResult(_service.CreatePaymentUrl(model, context));
        }
    }
}