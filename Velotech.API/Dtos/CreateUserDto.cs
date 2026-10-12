namespace Velotech.API.Dtos;

public class CreateUserDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>Mot de passe en clair (sera hashe cote serveur avec PBKDF2).</summary>
    public string Password { get; set; } = "";

    public int RoleId { get; set; }

    /// <summary>
    /// StoreId : requis pour un staff (Admin/Manager/Tech), null pour un Client.
    /// </summary>
    public int? StoreId { get; set; }
}
