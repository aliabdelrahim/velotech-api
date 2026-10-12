namespace Velotech.API.Dtos;

/// <summary>
/// DTO utilise par un ADMIN pour modifier n'importe quel utilisateur.
/// Different de UpdateProfileDto qui sert au user pour modifier son propre profil.
/// </summary>
public class UpdateUserDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    public int RoleId { get; set; }

    /// <summary>Null pour un Client, requis pour un staff.</summary>
    public int? StoreId { get; set; }

    /// <summary>
    /// Optionnel. Si fourni, remplace le mot de passe de l'utilisateur
    /// (reset manuel par l'admin, ex. utilisateur ayant perdu son mdp).
    /// </summary>
    public string? NewPassword { get; set; }
}
