namespace Velotech.API.Dtos;

/// <summary>
/// Payload envoye par l'utilisateur qui souhaite supprimer son propre compte.
/// Le mot de passe est requis pour verifier qu'il s'agit bien du titulaire
/// (securite / anti-detournement de compte).
/// </summary>
public class DeleteAccountDto
{
    public string Password { get; set; } = "";
}
