namespace Velotech.API.Models;

public class Payment
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public decimal Amount { get; set; }

    // "Order", "Rental", "Repair"
    public string PaymentType { get; set; } = "Order";

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public int? RentalId { get; set; }
    public Rental? Rental { get; set; }

    public int? RepairId { get; set; }
    public Repair? Repair { get; set; }

    // "Pending", "Paid", "Failed", "Refunded"
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
