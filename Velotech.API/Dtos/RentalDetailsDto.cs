namespace Velotech.API.Dtos;

public class RentalDetailsDto
{
    public int RentalId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";

    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";

    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = "";
}
