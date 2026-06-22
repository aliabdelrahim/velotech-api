namespace Velotech.API.Dtos;

public class UpdateProfileDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    // Optionnel : si rempli, change le mot de passe
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}
