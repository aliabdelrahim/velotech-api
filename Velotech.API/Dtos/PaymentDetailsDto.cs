namespace Velotech.API.Dtos;

public class PaymentDetailsDto
{
    public int PaymentId { get; set; }

    public int UserId { get; set; }
    public string UserName { get; set; } = "";

    public string PaymentType { get; set; } = ""; // Order / Rental / Repair
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";

    public int? OrderId { get; set; }
    public int? RentalId { get; set; }
    public int? RepairId { get; set; }

    public DateTime CreatedAt { get; set; }
}