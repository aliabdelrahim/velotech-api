namespace Velotech.API.Dtos;

public class RepairDetailsDto
{
    public int RepairId { get; set; }

    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";

    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";

    public int TechnicianId { get; set; }
    public string TechnicianName { get; set; } = "";

    public int? AppointmentId { get; set; }

    public string Status { get; set; } = "";
    public string? Diagnosis { get; set; }
    public string? WorkDone { get; set; }
    public decimal? Cost { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}