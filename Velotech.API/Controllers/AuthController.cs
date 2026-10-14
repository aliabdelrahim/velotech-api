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
    private readonly IWebHostEnvironment _env;

    public AuthController(VelotechDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _config = config;
        _env = env;
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

        // Chaque tentative "reserve" d'abord une des 5 places autorisees, en une seule
        // requete atomique (UPDATE ... WHERE compteur < 5 ET compte non bloque), AVANT
        // de verifier le mot de passe. Ainsi, meme 100 tentatives envoyees en meme temps
        // par un robot ne permettent d'essayer que 5 mots de passe.
        var now = DateTime.UtcNow;
        var reserved = await _db.Users
            .Where(u => u.Id == user.Id
                        && (u.LockoutEndAt == null || u.LockoutEndAt <= now)
                        && u.FailedLoginCount < MaxFailedLogins)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1));

        if (reserved == 0)
        {
            var lockoutEndAt = await _db.Users.Where(u => u.Id == user.Id)
                .Select(u => u.LockoutEndAt).FirstAsync();
            return LockedOut(lockoutEndAt > now ? lockoutEndAt.Value : now.Add(LockoutDuration));
        }

        if (!VerifyPassword(dto.Password, user.PasswordHash))
        {
            // 5e echec : blocage de 15 min (et compteur remis a zero pour la suite)
            var lockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
            var locked = await _db.Users
                .Where(u => u.Id == user.Id && u.FailedLoginCount >= MaxFailedLogins)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LockoutEndAt, lockoutEnd)
                    .SetProperty(u => u.FailedLoginCount, 0));

            return locked > 0 ? LockedOut(lockoutEnd) : Unauthorized("Invalid credentials.");
        }

        // Connexion reussie : remise a zero du compteur
        await _db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginCount, 0)
                .SetProperty(u => u.LockoutEndAt, (DateTime?)null));

        var token = CreateJwtToken(user);

        return Ok(token);
    }

    // Blocage apres echecs de connexion successifs
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private ObjectResult LockedOut(DateTime lockoutEndUtc)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((lockoutEndUtc - DateTime.UtcNow).TotalMinutes));
        Response.Headers.RetryAfter = (minutes * 60).ToString();
        return StatusCode(StatusCodes.Status429TooManyRequests, new
        {
            message = $"Too many failed attempts. Try again in {minutes} minute(s).",
            retryAfterMinutes = minutes
        });
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
            // Le token n'est expose (console + reponse) QU'EN DEVELOPPEMENT :
            // en production, le renvoyer permettrait a n'importe qui de
            // reinitialiser le mot de passe de n'importe quel compte (admin compris).
            if (_env.IsDevelopment())
            {
                Console.WriteLine($"[Velotech] Reset link for {user.Email} : /reset-password/{token}");
                return Ok(new
                {
                    message = "If the email exists, a reset link has been sent.",
                    devToken = token
                });
            }
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
        // Le titulaire du compte a prouve son identite par e-mail : on debloque.
        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
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