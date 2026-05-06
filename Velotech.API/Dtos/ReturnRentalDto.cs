namespace Velotech.API.Dtos;

public class ReturnRentalDto
{
    public DateTime? ReturnedAt { get; set; } // optionnel
    public string? Notes { get; set; }
}