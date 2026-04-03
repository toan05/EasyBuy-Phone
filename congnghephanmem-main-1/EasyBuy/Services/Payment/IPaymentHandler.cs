using EasyBuy.Models.VNPAY;

namespace EasyBuy.Services.Payment
{
    public interface IPaymentHandler
    {
        Task<string> HandleAsync(PaymentInformationModel model, HttpContext context);
    }
}