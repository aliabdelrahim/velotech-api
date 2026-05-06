namespace Velotech.API.Dtos;

public class CreateAppointmentDto
{
    public int UserId { get; set; }
    public int StoreId { get; set; }

    public int? ProductId { get; set; } // optionnel (vélo du client)
    public string ServiceType { get; set; } = "Entretien"; // Entretien / Réparation
    public DateTime ScheduledAt { get; set; }

    public string? Notes { get; set; }
}
