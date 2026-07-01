namespace EasyBuy.Models.MOMO
{
    public class MomoExecuteResponseModel
    {
        public int ErrorCode { get; set; }
        public string Message { get; set; }
        public string OrderId { get; set; }
        public string Amount { get; set; }
        public string FullName { get; set; }
        public string OrderInfo { get; set; }
    }
}
