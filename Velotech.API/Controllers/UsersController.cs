using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;
using Velotech.API.Security;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Tout user connecte par defaut; les endpoints admin ont leur propre attribut
public class UsersController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public UsersController(VelotechDbContext db)
    {
        _db = db;
    }

    // GET: api/users/me
    [HttpGet("me")]
    public async Task<ActionResult<UserDetailsDto>> GetMe()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized("User identifier missing from token.");

        var user = await _db.Users
            .Include(u => u.Role)
            .Include(u => u.Store)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound();

        return Ok(new UserDetailsDto
        {
            Id = user.Id,
            Name = user.Name ?? "",
            Email = user.Email ?? "",
            RoleId = user.RoleId,
            RoleName = user.Role?.Name ?? "",
            StoreId = user.StoreId ?? 0,
            StoreName = user.Store?.Name ?? ""
        });
    }

    // PUT: api/users/me
    [HttpPut("me")]
    public async Task<ActionResult<UserDetailsDto>> UpdateMe(UpdateProfileDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized("User identifier missing from token.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest("Email is required.");

        var user = await _db.Users
            .Include(u => u.Role)
            .Include(u => u.Store)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound();

        // Verifier que l'email n'est pas pris par un autre user
        var emailTaken = await _db.Users
            .AnyAsync(u => u.Email == dto.Email && u.Id != userId);
        if (emailTaken) return BadRequest("Email already taken by another account.");

        user.Name = dto.Name.Trim();
        user.Email = dto.Email.Trim();

        // Changement de mot de passe si demande
        if (!string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                return BadRequest("Current password is required to set a new one.");
            if (!PasswordPolicy.IsStrong(dto.NewPassword))
                return BadRequest(PasswordPolicy.ErrorMessage);

            if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
                !VerifyPassword(dto.CurrentPassword, user.PasswordHash))
            {
                return BadRequest("Current password is incorrect.");
            }
            user.PasswordHash = HashPassword(dto.NewPassword);
        }

        await _db.SaveChangesAsync();

        return Ok(new UserDetailsDto
        {
            Id = user.Id,
            Name = user.Name ?? "",
            Email = user.Email ?? "",
            RoleId = user.RoleId,
            RoleName = user.Role?.Name ?? "",
            StoreId = user.StoreId ?? 0,
            StoreName = user.Store?.Name ?? ""
        });
    }

    /// <summary>
    /// Desinscription : l'utilisateur authentifie supprime son propre compte.
    ///
    /// Pattern de soft delete + anonymisation RGPD :
    ///   1. Verifier le mot de passe (anti-detournement)
    ///   2. Anonymiser les PII (Name, Email) pour que plus aucune donnee
    ///      personnelle ne subsiste en base
    ///   3. Marquer le compte comme supprime (IsDeleted = true, IsActive = false)
    ///
    /// L'historique des commandes, locations et paiements est conserve pour
    /// les obligations comptables et fiscales (10 ans en Belgique), mais sans
    /// aucune donnee identifiante rattachee.
    /// </summary>
    [HttpDelete("me")]
    public async Task<ActionResult> DeleteMyAccount(DeleteAccountDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized("User identifier missing from token.");

        if (string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("Password confirmation is required to delete your account.");

        // On charge le user AVEC son role pour le controle metier
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return NotFound();

        // SECURITE METIER : interdiction pour les comptes de staff (Admin/Manager/Tech)
        // de s'auto-supprimer. Un admin qui se supprimerait rendrait le back-office
        // inaccessible. La suppression de ces comptes doit passer par un autre admin
        // (futur endpoint DELETE /api/users/{id} reserve au role Admin).
        var roleName = user.Role?.Name ?? "";
        if (roleName == "Admin" || roleName == "Manager" || roleName == "Tech")
        {
            return BadRequest(
                $"Les comptes {roleName} ne peuvent pas s'auto-supprimer. " +
                "Contactez un administrateur pour la suppression.");
        }

        // Verification du mot de passe (anti-detournement de compte)
        if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
            !VerifyPassword(dto.Password, user.PasswordHash))
        {
            return BadRequest("Incorrect password.");
        }

        // Anonymisation des donnees personnelles (RGPD "droit a l'oubli")
        user.Name = "Compte supprime";
        user.Email = $"deleted-{user.Id}@velotech.local";
        user.PasswordHash = null;
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAt = null;

        // Marquage soft delete
        user.IsActive = false;
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new { message = "Account deleted successfully." });
    }

    // --- Password hashing (PBKDF2) - meme format que AuthController ---
    private static string HashPassword(string password)
    {
        const int iterations = 100_000;
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        byte[] hash = pbkdf2.GetBytes(32);
        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out int iterations)) return false;
        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] expectedHash = Convert.FromBase64String(parts[2]);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        byte[] actualHash = pbkdf2.GetBytes(32);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    // POST: api/users  (Admin only — creation d'un compte interne par un admin)
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<UserDetailsDto>> CreateUser(CreateUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Le nom est requis.");

        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest("L'email est requis.");

        if (!PasswordPolicy.IsStrong(dto.Password))
            return BadRequest(PasswordPolicy.ErrorMessage);

        var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.Email);
        if (emailExists)
            return BadRequest("Cet email est deja utilise.");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == dto.RoleId);
        if (role == null) return BadRequest($"Role {dto.RoleId} introuvable.");

        // Un Client n'a pas de StoreId. Les autres roles (Admin/Manager/Tech) en ont un.
        Store? store = null;
        if (role.Name != "Client")
        {
            if (!dto.StoreId.HasValue)
                return BadRequest($"StoreId requis pour un compte {role.Name}.");

            store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == dto.StoreId.Value);
            if (store == null) return BadRequest($"Magasin {dto.StoreId.Value} introuvable.");
        }

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = dto.Email.Trim(),
            PasswordHash = HashPassword(dto.Password),
            IsActive = true,
            StoreId = role.Name == "Client" ? null : dto.StoreId,
            RoleId = dto.RoleId
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = new UserDetailsDto
        {
            Id = user.Id,
            Name = user.Name ?? "",
            Email = user.Email ?? "",
            StoreId = store?.Id ?? 0,
            StoreName = store?.Name ?? "",
            RoleId = role.Id,
            RoleName = role.Name ?? ""
        };

        return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, result);
    }

    // PUT: api/users/5  (Admin only — modification complete d'un utilisateur)
    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDetailsDto>> UpdateUser(int id, UpdateUserDto dto)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Le nom est requis.");
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest("L'email est requis.");

        // Email unique (hors soi-meme)
        var emailTaken = await _db.Users.AnyAsync(u => u.Email == dto.Email && u.Id != id);
        if (emailTaken) return BadRequest("Cet email est deja utilise par un autre compte.");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == dto.RoleId);
        if (role == null) return BadRequest($"Role {dto.RoleId} introuvable.");

        // Validation StoreId selon le role
        if (role.Name != "Client")
        {
            if (!dto.StoreId.HasValue)
                return BadRequest($"StoreId requis pour un compte {role.Name}.");
            var storeExists = await _db.Stores.AnyAsync(s => s.Id == dto.StoreId.Value);
            if (!storeExists) return BadRequest($"Magasin {dto.StoreId.Value} introuvable.");
        }

        user.Name = dto.Name.Trim();
        user.Email = dto.Email.Trim();
        user.RoleId = dto.RoleId;
        user.StoreId = role.Name == "Client" ? null : dto.StoreId;

        // Reset mot de passe optionnel (par admin, ex. user ayant perdu son mdp)
        if (!string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            if (!PasswordPolicy.IsStrong(dto.NewPassword))
                return BadRequest(PasswordPolicy.ErrorMessage);
            user.PasswordHash = HashPassword(dto.NewPassword);
        }

        await _db.SaveChangesAsync();

        // Retour avec donnees fraiches
        var updated = await _db.Users
            .Include(u => u.Role)
            .Include(u => u.Store)
            .FirstAsync(u => u.Id == id);

        return Ok(new UserDetailsDto
        {
            Id = updated.Id,
            Name = updated.Name ?? "",
            Email = updated.Email ?? "",
            RoleId = updated.RoleId,
            RoleName = updated.Role?.Name ?? "",
            StoreId = updated.StoreId ?? 0,
            StoreName = updated.Store?.Name ?? "",
            IsDeleted = updated.IsDeleted
        });
    }

    // DELETE: api/users/5  (Admin only — suppression soft + anonymisation RGPD)
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteUser(int id)
    {
        // Protection : l'admin connecte ne peut pas se supprimer lui-meme
        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(currentUserIdStr, out var currentUserId) && currentUserId == id)
            return BadRequest("Vous ne pouvez pas supprimer votre propre compte depuis cet endpoint.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        if (user.IsDeleted)
            return BadRequest("Ce compte est deja supprime.");

        // Meme pattern que l'auto-suppression client : anonymisation + soft delete
        user.Name = "Compte supprime";
        user.Email = $"deleted-{user.Id}@velotech.local";
        user.PasswordHash = null;
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAt = null;
        user.IsActive = false;
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // GET: api/users/5  (Admin only — un user doit utiliser /me pour son propre profil)
    [Authorize(Roles = "Admin")]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDetailsDto>> GetUserById(int id)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .Include(u => u.Store)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        var dto = new UserDetailsDto
        {
            Id = user.Id,
            Name = user.Name ?? "",
            Email = user.Email ?? "",
            RoleId = user.RoleId,
            RoleName = user.Role?.Name ?? "",
            StoreId = user.StoreId ?? 0,
            StoreName = user.Store?.Name ?? "",
            IsDeleted = user.IsDeleted,
            DeletedAt = user.DeletedAt
        };

        return Ok(dto);
    }

    // GET: api/users?storeId=1  (Admin only — liste de tous les utilisateurs)
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<ActionResult<List<UserDetailsDto>>> GetUsers([FromQuery] int? storeId)
    {
        var query = _db.Users
            .Include(u => u.Role)
            .Include(u => u.Store)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(u => u.StoreId == storeId.Value);

        var users = await query
            .OrderBy(u => u.Id)
            .Select(u => new UserDetailsDto
            {
                Id = u.Id,
                Name = u.Name ?? "",
                Email = u.Email ?? "",
                RoleId = u.RoleId,
                RoleName = u.Role != null ? u.Role.Name! : "",
                StoreId = u.StoreId ?? 0,
                StoreName = u.Store != null ? u.Store.Name! : "",
                IsDeleted = u.IsDeleted,
                DeletedAt = u.DeletedAt
            })
            .ToListAsync();

        return Ok(users);
    }
}