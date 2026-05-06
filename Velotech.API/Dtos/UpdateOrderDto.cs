namespace Velotech.API.Dtos
{
    public class UpdateOrderDto
    {
        public int OrderId { get; set; }
        public string Status { get; set; } = "";   // Pending, Paid, Shipped…
    }
}
