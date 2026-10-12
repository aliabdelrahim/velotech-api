namespace Velotech.API.Dtos;

public class CreateRepairDto
{
    public int StoreId { get; set; }
    public int ProductId { get; set; }      // vélo concerné
    public int TechnicianId { get; set; }   // user technicien

    public int? AppointmentId { get; set; } // optionnel
    public string? Diagnosis { get; set; }

    /// <summary>
    /// Si true (defaut), la reparation est creee directement en "InProgress"
    /// avec StartedAt=now. Correspond au flux "le tech prend en charge le jour J
    /// et commence immediatement". Mettre false pour creer en "Open" (file d'attente).
    /// </summary>
    public bool AutoStart { get; set; } = true;
}
