using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly VelotechDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(VelotechDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // POST: api/auth/register
    [HttpPost("register")]
    public async Task<ActionResult> Register(RegisterDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");

        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest("Email is required.");

        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
            return BadRequest("Password must be at least 6 characters.");

        var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.Email);
        if (emailExists)
            return BadRequest("Email already exists.");

        var role = await _db.Roles.FirstAsync(r => r.Name == "Client");

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = dto.Email.Trim(),
            PasswordHash = HashPassword(dto.Password),
            RoleId = role.Id,
            StoreId = null
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new { message = "User registered", userId = user.Id });
    }

    // POST: api/auth/login
    [HttpPost("login")]
    public async Task<ActionResult<AuthResultDto>> Login(LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("Email and password are required.");

        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null) return Unauthorized("Invalid credentials.");

        // Les comptes supprimes (desinscription) ou desactives ne peuvent plus se connecter.
        // On retourne le meme message que pour "mauvais identifiants" pour ne pas
        // divulguer l'existence du compte.
        if (user.IsDeleted || !user.IsActive)
            return Unauthorized("Invalid credentials.");

        // Si tu as des anciens users en DB sans PasswordHash
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
            return Unauthorized("User has no password set. Please register a new user.");

        if (!VerifyPassword(dto.Password, user.PasswordHash))
            return Unauthorized("Invalid credentials.");

        var token = CreateJwtToken(user);

        return Ok(token);
    }

    // POST: api/auth/forgot-password
    // Genere un token de reset et le retourne (en prod : envoye par email).
    // Reponse identique que l'email existe ou non : empeche l'enumeration.
    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest("Email is required.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
        if (user != null)
        {
            // Token aleatoire 32 bytes -> base64 url-safe
            byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
            string token = Convert.ToBase64String(tokenBytes)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            user.PasswordResetToken = HashToken(token);
            user.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(30);
            await _db.SaveChangesAsync();

            // TODO: en production, envoyer un email avec un lien :
            //       https://velotech.com/reset-password/{token}
            //       Pour le dev/TFE, on log le lien en console.
            Console.WriteLine($"[Velotech] Reset link for {user.Email} : /reset-password/{token}");

            // Le token est retourne EN DEV uniquement pour faciliter les tests.
            // En production, retirer ce champ.
            return Ok(new
            {
                message = "If the email exists, a reset link has been sent.",
                devToken = token // a retirer en prod
            });
        }

        // Reponse identique meme si l'email n'existe pas (anti-enumeration)
        return Ok(new { message = "If the email exists, a reset link has been sent." });
    }

    // POST: api/auth/reset-password
    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword(ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
            return BadRequest("Token and new password are required.");
        if (dto.NewPassword.Length < 6)
            return BadRequest("New password must be at least 6 characters.");

        var hashedToken = HashToken(dto.Token);

        var user = await _db.Users
            .FirstOrDefaultAsync(u =>
                u.PasswordResetToken == hashedToken &&
                u.PasswordResetExpiresAt != null &&
                u.PasswordResetExpiresAt > DateTime.UtcNow);

        if (user == null)
            return BadRequest("Invalid or expired token.");

        user.PasswordHash = HashPassword(dto.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAt = null;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password has been reset successfully." });
    }

    // Hash du token (SHA-256) - on ne stocke jamais le token en clair
    private static string HashToken(string token)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }

    // --- JWT helpers ---
    private AuthResultDto CreateJwtToken(User user)
    {
        var jwt = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name ?? ""),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Role, user.Role?.Name ?? "User")
        };

        // ✅ storeId seulement si l’utilisateur en a un (employé)
        if (user.StoreId.HasValue)
        {
            claims.Add(new Claim("storeId", user.StoreId.Value.ToString()));
        }

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(4);

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return new AuthResultDto
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAtUtc = expires,
            UserId = user.Id,
            Role = user.Role?.Name ?? "User",
            StoreId = user.StoreId // ✅ garde null pour un client
        };
    }

    // --- Password hashing (PBKDF2) ---
    // Format: {iterations}.{saltBase64}.{hashBase64}
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
}