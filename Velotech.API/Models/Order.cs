namespace Velotech.API.Models;

public class Order
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }

    public List<OrderItem>? OrderItems { get; set; }
}
