using EasyBuy.Models.VNPAY;

namespace EasyBuy.Services.Payment
{
    public class CodHandler : IPaymentHandler
    {
        public Task<string> HandleAsync(PaymentInformationModel model, HttpContext context)
        {
            // Trả về một mã đặc biệt để Controller nhận diện
            return Task.FromResult("INTERNAL_COD");
        }
    }
}