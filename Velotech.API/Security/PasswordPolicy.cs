namespace Velotech.API.Security;

/// <summary>
/// Regle unique de robustesse des mots de passe (inscription, reinitialisation,
/// changement, creation par l'admin) : 8 caracteres minimum, au moins une lettre
/// et au moins un chiffre. Le front applique la meme regle pour un retour immediat.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public const string ErrorMessage =
        "Password must be at least 8 characters and contain at least one letter and one digit.";

    public static bool IsStrong(string? password) =>
        !string.IsNullOrWhiteSpace(password)
        && password.Length >= MinLength
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);
}
