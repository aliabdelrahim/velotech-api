namespace Velotech.API.Models;

/// <summary>
/// Notification in-app affichee a l'utilisateur dans la cloche du header.
/// Exemple : "Votre reparation est terminee (50 EUR)".
/// </summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>Destinataire de la notification.</summary>
    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Titre court affiche en gras.</summary>
    public string Title { get; set; } = "";

    /// <summary>Corps du message (plus detaille).</summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Type metier : "RepairCompleted", "OrderConfirmed", "RentalReturn"...
    /// Permet au front de choisir une icone ou un style.
    /// </summary>
    public string Type { get; set; } = "Info";

    /// <summary>Lien interne vers lequel rediriger au clic (optionnel).</summary>
    public string? Link { get; set; }

    /// <summary>True quand l'utilisateur a lu la notification.</summary>
    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
