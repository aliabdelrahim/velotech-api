namespace Velotech.API.Models;

public class Repair
{
    public int Id { get; set; }

    // Magasin
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    // Vélo concerné
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    // Technicien assigné (User avec rôle Technicien)
    public int TechnicianId { get; set; }
    public User? Technician { get; set; }

    // Lien optionnel avec une réservation
    public int? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }

    public string? Diagnosis { get; set; }
    public string? WorkDone { get; set; }

    // "Open", "InProgress", "Done", "Cancelled"
    public string Status { get; set; } = "Open";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public decimal? Cost { get; set; }
}