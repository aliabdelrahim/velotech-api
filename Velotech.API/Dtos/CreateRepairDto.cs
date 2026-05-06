namespace Velotech.API.Dtos;

public class CreateRepairDto
{
    public int StoreId { get; set; }
    public int ProductId { get; set; }      // vélo concerné
    public int TechnicianId { get; set; }   // user technicien

    public int? AppointmentId { get; set; } // optionnel
    public string? Diagnosis { get; set; }
}
