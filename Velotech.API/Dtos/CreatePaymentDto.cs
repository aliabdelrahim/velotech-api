namespace Velotech.API.Dtos;

public class CreatePaymentDto
{
    public int UserId { get; set; }

    public int? OrderId { get; set; }
    public int? RentalId { get; set; }
    public int? RepairId { get; set; }

    public decimal Amount { get; set; }
}