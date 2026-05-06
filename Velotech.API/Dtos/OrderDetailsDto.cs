namespace Velotech.API.Dtos
{
    public class OrderDetailsDto
    {
        public int OrderId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }

        public string StoreName { get; set; } = "";
        public string CustomerName { get; set; } = "";

        public List<OrderItemDto> Items { get; set; } = new();
    }
    public class OrderItemDto
    {
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
