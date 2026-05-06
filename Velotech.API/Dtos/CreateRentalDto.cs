namespace Velotech.API.Dtos;

public class CreateRentalDto
{
    public int UserId { get; set; }
    public int StoreId { get; set; }
    public int ProductId { get; set; } // vélo
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
