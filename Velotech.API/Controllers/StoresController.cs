using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StoresController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public StoresController(VelotechDbContext db)
    {
        _db = db;
    }

    // POST: api/stores  ✅ Admin only
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<StoreDetailsDto>> CreateStore(CreateStoreDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");

        var exists = await _db.Stores.AnyAsync(s => s.Name == dto.Name);
        if (exists) return BadRequest("Store name already exists.");

        var store = new Store
        {
            Name = dto.Name.Trim(),
            Address = dto.Address?.Trim() ?? ""
        };

        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = new StoreDetailsDto
        {
            Id = store.Id,
            Name = store.Name ?? "",
            Address = store.Address ?? ""
        };

        return CreatedAtAction(nameof(GetStoreById), new { id = store.Id }, result);
    }

    // GET: api/stores/1  ✅ Public
    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<StoreDetailsDto>> GetStoreById(int id)
    {
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == id);
        if (store == null) return NotFound();

        return Ok(new StoreDetailsDto
        {
            Id = store.Id,
            Name = store.Name ?? "",
            Address = store.Address ?? ""
        });
    }

    // GET: api/stores  ✅ Public
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<List<StoreDetailsDto>>> GetStores()
    {
        var stores = await _db.Stores
            .OrderBy(s => s.Id)
            .Select(s => new StoreDetailsDto
            {
                Id = s.Id,
                Name = s.Name ?? "",
                Address = s.Address ?? ""
            })
            .ToListAsync();

        return Ok(stores);
    }
}