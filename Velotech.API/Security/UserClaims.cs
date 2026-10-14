using System.Security.Claims;

namespace Velotech.API.Security;

/// <summary>
/// Regles d'acces communes aux controleurs :
///  - un client ne lit / modifie que SES donnees (id lu dans le JWT, jamais dans le body),
///  - le staff (Admin, Manager, Tech) a acces a tout pour le back-office.
/// </summary>
public static class UserClaims
{
    /// <summary>Roles du personnel, pour [Authorize(Roles = UserClaims.StaffRoles)].</summary>
    public const string StaffRoles = "Admin,Manager,Tech,Technicien";

    public static int? GetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static bool IsStaff(this ClaimsPrincipal user) =>
        StaffRoles.Split(',').Any(user.IsInRole);

    /// <summary>Vrai si l'utilisateur est le proprietaire de la ressource ou membre du staff.</summary>
    public static bool CanAccess(this ClaimsPrincipal user, int ownerId) =>
        user.IsStaff() || user.GetUserId() == ownerId;
}
