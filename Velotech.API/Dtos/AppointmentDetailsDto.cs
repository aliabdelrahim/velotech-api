namespace Velotech.API.Dtos;

public class AppointmentDetailsDto
{
    public int AppointmentId { get; set; }

    public int UserId { get; set; }
    public string UserName { get; set; } = "";

    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";

    public int? ProductId { get; set; }
    public string ProductName { get; set; } = "";

    public string ServiceType { get; set; } = "";
    public DateTime ScheduledAt { get; set; }
    public string Status { get; set; } = "";

    public string? Notes { get; set; }
}
