namespace Velotech.API.Models;

/// <summary>
/// Entites qui supportent la suppression douce (soft delete).
/// Au lieu d'etre effacees physiquement de la base, elles sont marquees
/// comme supprimees via IsDeleted et conservent leur historique pour :
///   - la tracabilite comptable (RGPD, obligations fiscales 10 ans),
///   - l'integrite referentielle (commandes passees qui pointent vers un produit),
///   - la restauration en cas d'erreur operationnelle.
/// Les requetes de lecture filtrent automatiquement ces entites via les
/// Global Query Filters configures dans VelotechDbContext.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}
