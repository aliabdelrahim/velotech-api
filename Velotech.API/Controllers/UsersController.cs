using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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