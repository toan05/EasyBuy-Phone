using EasyBuy.Services.MOMO;
using EasyBuy.Services.VNPAY;

namespace EasyBuy.Services.Payment
{
    public class PaymentFactory
    {
        private readonly IServiceProvider _serviceProvider;
        public PaymentFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        public IPaymentHandler GetHandler(int methodId)
        {
            return methodId switch
            {
                1 => new CodHandler(),
                2 => new VnpayHandler(_serviceProvider.GetRequiredService<IVnPayService>(), _serviceProvider),
                3 => new MomoHandler(_serviceProvider.GetRequiredService<IMomoService>(), _serviceProvider),
                1002 => new MomoHandler(_serviceProvider.GetRequiredService<IMomoService>(), _serviceProvider), // For backward compatibility
                _ => throw new ArgumentException("Phương thức thanh toán không hợp lệ")
            };
        }
    }
}