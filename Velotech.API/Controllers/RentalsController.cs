using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RentalsController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public RentalsController(VelotechDbContext db)
    {
        _db = db;
    }

    // POST: api/rentals
    [HttpPost]
    public async Task<ActionResult<RentalDetailsDto>> CreateRental(CreateRentalDto dto)
    {
        // 1) Validations dates
        if (dto.StartDate.Date < DateTime.UtcNow.Date)
            return BadRequest("StartDate must be today or later.");

        if (dto.EndDate <= dto.StartDate)
            return BadRequest("EndDate must be after StartDate.");

        // 2) Store
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == dto.StoreId);
        if (store == null) return NotFound($"Store {dto.StoreId} not found.");

        // 3) User
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId);
        if (user == null) return NotFound($"User {dto.UserId} not found.");

        // Règle métier : user appartient au magasin
        if (user.StoreId != dto.StoreId)
            return BadRequest("User does not belong to this store.");

        // 4) Product
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId);
        if (product == null) return NotFound($"Product {dto.ProductId} not found.");

        if (!product.IsRentable)
            return BadRequest("This product is not rentable.");

        if (!string.Equals(product.Type, "Bike", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only bikes can be rented.");

        // 5) Stock rental dans le magasin (StoreProducts)
        var sp = await _db.StoreProducts
            .FirstOrDefaultAsync(x => x.StoreId == dto.StoreId && x.ProductId == dto.ProductId);

        if (sp == null)
            return BadRequest($"ProductId {dto.ProductId} is not available in store {dto.StoreId}.");

        if (sp.StockRental <= 0)
            return BadRequest("No rental stock available for this bike in this store.");

        // 6) Disponibilité sur période (overlap)
        var hasOverlap = await _db.Rentals.AnyAsync(r =>
            r.StoreId == dto.StoreId &&
            r.ProductId == dto.ProductId &&
            r.Status != "Cancelled" &&
            r.StartDate < dto.EndDate &&
            dto.StartDate < r.EndDate);

        if (hasOverlap)
            return BadRequest("This bike is already rented for the selected period.");

        // 7) Calcul prix
        if (product.PriceRental == null || product.PriceRental <= 0)
            return BadRequest("This bike has no valid rental price.");

        var days = (dto.EndDate.Date - dto.StartDate.Date).Days;
        if (days <= 0) days = 1;

        var total = product.PriceRental.Value * days;

        // 8) Création Rental + décrément stock rental
        var rental = new Rental
        {
            UserId = dto.UserId,
            StoreId = dto.StoreId,
            ProductId = dto.ProductId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            TotalPrice = total,
            Status = "Confirmed",
            CreatedAt = DateTime.UtcNow
        };

        sp.StockRental -= 1;

        _db.Rentals.Add(rental);
        await _db.SaveChangesAsync();

        // ✅ Retour DTO (propre)
        var result = new RentalDetailsDto
        {
            RentalId = rental.Id,
            UserId = user.Id,
            UserName = user.Name ?? "",
            StoreId = store.Id,
            StoreName = store.Name ?? "",
            ProductId = product.Id,
            ProductName = product.Name ?? "",
            StartDate = rental.StartDate,
            EndDate = rental.EndDate,
            TotalPrice = rental.TotalPrice,
            Status = rental.Status ?? ""
        };

        return CreatedAtAction(nameof(GetRentalById), new { id = rental.Id }, result);
    }

    // GET: api/rentals/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RentalDetailsDto>> GetRentalById(int id)
    {
        var rental = await _db.Rentals
            .Include(r => r.User)
            .Include(r => r.Store)
            .Include(r => r.Product)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (rental == null) return NotFound();

        var dto = new RentalDetailsDto
        {
            RentalId = rental.Id,
            UserId = rental.UserId,
            UserName = rental.User?.Name ?? "",
            StoreId = rental.StoreId,
            StoreName = rental.Store?.Name ?? "",
            ProductId = rental.ProductId,
            ProductName = rental.Product?.Name ?? "",
            StartDate = rental.StartDate,
            EndDate = rental.EndDate,
            TotalPrice = rental.TotalPrice,
            Status = rental.Status ?? ""
        };

        return Ok(dto);
    }

    // GET: api/rentals?storeId=1&userId=2&status=Confirmed
    [HttpGet]
    public async Task<ActionResult<List<RentalDetailsDto>>> GetRentals(
        [FromQuery] int? storeId,
        [FromQuery] int? userId,
        [FromQuery] string? status)
    {
        var query = _db.Rentals
            .Include(r => r.User)
            .Include(r => r.Store)
            .Include(r => r.Product)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(r => r.StoreId == storeId.Value);

        if (userId.HasValue)
            query = query.Where(r => r.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        var rentals = await query
            .OrderByDescending(r => r.Id)
            .Select(r => new RentalDetailsDto
            {
                RentalId = r.Id,
                UserId = r.UserId,
                UserName = r.User != null ? r.User.Name ?? "" : "",
                StoreId = r.StoreId,
                StoreName = r.Store != null ? r.Store.Name ?? "" : "",
                ProductId = r.ProductId,
                ProductName = r.Product != null ? r.Product.Name ?? "" : "",
                StartDate = r.StartDate,
                EndDate = r.EndDate,
                TotalPrice = r.TotalPrice,
                Status = r.Status ?? ""
            })
            .ToListAsync();

        return Ok(rentals);
    }

    // POST: api/rentals/5/cancel
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelRental(int id, CancelRentalDto dto)
    {
        var rental = await _db.Rentals.FirstOrDefaultAsync(r => r.Id == id);
        if (rental == null) return NotFound();

        if (rental.Status == "Cancelled")
            return BadRequest("Rental is already cancelled.");

        if (rental.Status == "Completed")
            return BadRequest("Completed rental cannot be cancelled.");

        rental.Status = "Cancelled";

        // ✅ remettre le stock rental +1
        var sp = await _db.StoreProducts
            .FirstOrDefaultAsync(x => x.StoreId == rental.StoreId && x.ProductId == rental.ProductId);

        if (sp != null)
            sp.StockRental += 1;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST: api/rentals/5/return
    [HttpPost("{id:int}/return")]
    public async Task<IActionResult> ReturnRental(int id, ReturnRentalDto dto)
    {
        var rental = await _db.Rentals.FirstOrDefaultAsync(r => r.Id == id);
        if (rental == null) return NotFound();

        if (rental.Status == "Cancelled")
            return BadRequest("Cancelled rental cannot be returned.");

        if (rental.Status == "Completed")
            return BadRequest("Rental is already completed.");

        rental.Status = "Completed";

        // ✅ (version scolaire) : quand on retourne le vélo, on remet le stock rental +1
        var sp = await _db.StoreProducts
            .FirstOrDefaultAsync(x => x.StoreId == rental.StoreId && x.ProductId == rental.ProductId);

        if (sp != null)
            sp.StockRental += 1;

        await _db.SaveChangesAsync();
        return NoContent();
    }
}