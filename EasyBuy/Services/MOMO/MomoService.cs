using EasyBuy.Models;
using EasyBuy.Models.MOMO;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Crmf;
using System.Security.Cryptography;
using System.Text;
using RestSharp;

namespace EasyBuy.Services.MOMO
{
    public class MomoService : IMomoService
    {
        private readonly IOptions<MomoOptionModel> _options;
        public MomoService(IOptions<MomoOptionModel> options)
        {
            _options = options;
        }
        public async Task<MomoCreatePaymentResponseModel> CreatePaymentAsync(OrderInfo model)
        {
            if (string.IsNullOrWhiteSpace(model.OrderId))
            {
                model.OrderId = DateTime.UtcNow.Ticks.ToString();
            }
            model.OrderInfomation = "Khách hàng: " + model.FullName + ". Nội dung: " + model.OrderInfomation;
            var rawData =
                $"accessKey={_options.Value.AccessKey}" +
                $"&amount={model.Amount}" +
                $"&extraData=" +
                $"&ipnUrl={_options.Value.NotifyUrl}" +
                $"&orderId={model.OrderId}" +
                $"&orderInfo={model.OrderInfomation}" +
                $"&partnerCode={_options.Value.PartnerCode}" +
                $"&redirectUrl={_options.Value.ReturnUrl}" +
                $"&requestId={model.OrderId}" +
                $"&requestType={_options.Value.RequestType}";

            var signature = ComputeHmacSha256(rawData, _options.Value.SecretKey);

            var client = new RestClient(new RestClientOptions(_options.Value.MomoApiUrl)
            {
                ThrowOnAnyError = false,
                MaxTimeout = 30_000
            });
            var request = new RestRequest() { Method = RestSharp.Method.Post };

            request.AddHeader("Content-Type", "application/json; charset=UTF-8");
            request.AddHeader("Accept", "application/json");

            request.AddJsonBody(new
            {
                partnerCode = _options.Value.PartnerCode,
                accessKey = _options.Value.AccessKey,
                requestId = model.OrderId,
                amount = model.Amount.ToString("0"),
                orderId = model.OrderId,
                orderInfo = model.OrderInfomation,
                redirectUrl = _options.Value.ReturnUrl,
                ipnUrl = _options.Value.NotifyUrl,
                extraData = "",
                requestType = _options.Value.RequestType,
                signature = signature,
                lang = "vi"
            });

            var response = await client.ExecuteAsync(request);
            var content = response.Content ?? string.Empty;

            if (!response.IsSuccessful)
            {
                return new MomoCreatePaymentResponseModel
                {
                    ErrorCode = response.StatusCode == System.Net.HttpStatusCode.OK ? -1 : (int)response.StatusCode,
                    Message = $"Lỗi API MoMo: {response.StatusDescription ?? "Không có mô tả"}. Nội dung: {content}",
                    PayUrl = string.Empty
                };
            }

            var momoResponse = JsonConvert.DeserializeObject<MomoCreatePaymentResponseModel>(content);
            return momoResponse ?? new MomoCreatePaymentResponseModel
            {
                ErrorCode = -1,
                Message = "Không thể phân tích nội dung phản hồi từ MoMo.",
                PayUrl = string.Empty
            };

        }

        public async Task<MomoCreatePaymentResponseModel> CreatePaymentMomo(OrderInfo model)
        {
            return await CreatePaymentAsync(model);
        }

        public MomoExecuteResponseModel PaymentExecuteAsync(IQueryCollection collection)
        {
            var errorCode = collection.TryGetValue("errorCode", out var errorCodeValue) && int.TryParse(errorCodeValue, out var parsedErrorCode)
                ? parsedErrorCode
                : 0;
            var message = collection.TryGetValue("message", out var messageValue) ? messageValue.ToString() : string.Empty;
            var amount = collection.TryGetValue("amount", out var amountValue) ? amountValue.ToString() : string.Empty;
            var orderInfo = collection.TryGetValue("orderInfo", out var orderInfoValue) ? orderInfoValue.ToString() : string.Empty;
            var orderId = collection.TryGetValue("orderId", out var orderIdValue) ? orderIdValue.ToString() : string.Empty;

            return new MomoExecuteResponseModel()
            {
                ErrorCode = errorCode,
                Message = message,
                Amount = amount,
                OrderId = orderId,
                FullName = collection.TryGetValue("extraData", out var extraDataValue) ? extraDataValue.ToString() : string.Empty,
                OrderInfo = orderInfo
            };
        }


        private string ComputeHmacSha256(string message, string secretKey)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var messageBytes = Encoding.UTF8.GetBytes(message);

            byte[] hashBytes;

            using (var hmac = new HMACSHA256(keyBytes))
            {
                hashBytes = hmac.ComputeHash(messageBytes);
            }

            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            return hashString;
        }
    }

}