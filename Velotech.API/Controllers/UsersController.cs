using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public UsersController(VelotechDbContext db)
    {
        _db = db;
    }

    // GET: api/users/me
    [Authorize]
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
    [Authorize]
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
            if (dto.NewPassword.Length < 6)
                return BadRequest("New password must be at least 6 characters.");

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

    // POST: api/users
    [HttpPost]
    public async Task<ActionResult<UserDetailsDto>> CreateUser(CreateUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");

        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest("Email is required.");

        // Email unique (recommandé)
        var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.Email);
        if (emailExists)
            return BadRequest("Email already exists.");

        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == dto.StoreId);
        if (store == null) return NotFound($"Store {dto.StoreId} not found.");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == dto.RoleId);
        if (role == null) return NotFound($"Role {dto.RoleId} not found.");

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = dto.Email.Trim(),
            StoreId = dto.StoreId,
            RoleId = dto.RoleId
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = new UserDetailsDto
        {
            Id = user.Id,
            Name = user.Name ?? "",
            Email = user.Email ?? "",
            StoreId = store.Id,
            StoreName = store.Name ?? "",
            RoleId = role.Id,
            RoleName = role.Name ?? ""
        };

        return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, result);
    }

    // GET: api/users/5
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
            StoreName = user.Store?.Name ?? ""
        };

        return Ok(dto);
    }

    // GET: api/users?storeId=1 (optionnel mais utile)
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
                StoreName = u.Store != null ? u.Store.Name! : ""
            })
            .ToListAsync();

        return Ok(users);
    }
}