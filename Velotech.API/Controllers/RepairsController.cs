using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RepairsController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public RepairsController(VelotechDbContext db)
    {
        _db = db;
    }

    // POST: api/repairs
    [HttpPost]
    public async Task<ActionResult<RepairDetailsDto>> CreateRepair(CreateRepairDto dto)
    {
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == dto.StoreId);
        if (store == null) return NotFound($"Store {dto.StoreId} not found.");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId);
        if (product == null) return NotFound($"Product {dto.ProductId} not found.");

        if (!string.Equals(product.Type, "Bike", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only bikes can be repaired (Product.Type must be 'Bike').");

        var tech = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == dto.TechnicianId);

        if (tech == null) return NotFound($"Technician {dto.TechnicianId} not found.");

        // règle métier : technicien appartient au magasin
        if (tech.StoreId != dto.StoreId)
            return BadRequest("Technician does not belong to this store.");

        // optionnel : vérifier que le rôle est technicien (si tu as ce rôle)
        if (tech.Role != null && !string.Equals(tech.Role.Name, "Technicien", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Selected user is not a Technician.");

        Appointment? appointment = null;
        if (dto.AppointmentId.HasValue)
        {
            appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == dto.AppointmentId.Value);
            if (appointment == null) return NotFound($"Appointment {dto.AppointmentId.Value} not found.");

            if (appointment.StoreId != dto.StoreId)
                return BadRequest("Appointment does not belong to this store.");
        }

        var repair = new Repair
        {
            StoreId = dto.StoreId,
            ProductId = dto.ProductId,
            TechnicianId = dto.TechnicianId,
            AppointmentId = dto.AppointmentId,
            Diagnosis = dto.Diagnosis,
            Status = "Open",
            CreatedAt = DateTime.UtcNow
        };

        _db.Repairs.Add(repair);
        await _db.SaveChangesAsync();

        var result = new RepairDetailsDto
        {
            RepairId = repair.Id,
            StoreId = store.Id,
            StoreName = store.Name ?? "",
            ProductId = product.Id,
            ProductName = product.Name ?? "",
            TechnicianId = tech.Id,
            TechnicianName = tech.Name ?? "",
            AppointmentId = repair.AppointmentId,
            Status = repair.Status,
            Diagnosis = repair.Diagnosis,
            WorkDone = repair.WorkDone,
            Cost = repair.Cost,
            CreatedAt = repair.CreatedAt,
            StartedAt = repair.StartedAt,
            CompletedAt = repair.CompletedAt
        };

        return CreatedAtAction(nameof(GetRepairById), new { id = repair.Id }, result);
    }

    // GET: api/repairs/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RepairDetailsDto>> GetRepairById(int id)
    {
        var repair = await _db.Repairs
            .Include(r => r.Store)
            .Include(r => r.Product)
            .Include(r => r.Technician)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (repair == null) return NotFound();

        return Ok(new RepairDetailsDto
        {
            RepairId = repair.Id,
            StoreId = repair.StoreId,
            StoreName = repair.Store?.Name ?? "",
            ProductId = repair.ProductId,
            ProductName = repair.Product?.Name ?? "",
            TechnicianId = repair.TechnicianId,
            TechnicianName = repair.Technician?.Name ?? "",
            AppointmentId = repair.AppointmentId,
            Status = repair.Status,
            Diagnosis = repair.Diagnosis,
            WorkDone = repair.WorkDone,
            Cost = repair.Cost,
            CreatedAt = repair.CreatedAt,
            StartedAt = repair.StartedAt,
            CompletedAt = repair.CompletedAt
        });
    }

    // GET: api/repairs?storeId=1&status=Open
    [HttpGet]
    public async Task<ActionResult<List<RepairDetailsDto>>> GetRepairs([FromQuery] int? storeId, [FromQuery] string? status)
    {
        var query = _db.Repairs
            .Include(r => r.Store)
            .Include(r => r.Product)
            .Include(r => r.Technician)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(r => r.StoreId == storeId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        var items = await query
            .OrderByDescending(r => r.Id)
            .Select(r => new RepairDetailsDto
            {
                RepairId = r.Id,
                StoreId = r.StoreId,
                StoreName = r.Store != null ? r.Store.Name! : "",
                ProductId = r.ProductId,
                ProductName = r.Product != null ? r.Product.Name! : "",
                TechnicianId = r.TechnicianId,
                TechnicianName = r.Technician != null ? r.Technician.Name! : "",
                AppointmentId = r.AppointmentId,
                Status = r.Status,
                Diagnosis = r.Diagnosis,
                WorkDone = r.WorkDone,
                Cost = r.Cost,
                CreatedAt = r.CreatedAt,
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    // POST: api/repairs/5/start
    [HttpPost("{id:int}/start")]
    public async Task<IActionResult> StartRepair(int id, StartRepairDto dto)
    {
        var repair = await _db.Repairs.FirstOrDefaultAsync(r => r.Id == id);
        if (repair == null) return NotFound();

        if (repair.Status == "Cancelled")
            return BadRequest("Cancelled repair cannot be started.");

        if (repair.Status == "Done")
            return BadRequest("Repair is already completed.");

        repair.Status = "InProgress";
        repair.StartedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST: api/repairs/5/complete
    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> CompleteRepair(int id, CompleteRepairDto dto)
    {
        var repair = await _db.Repairs.FirstOrDefaultAsync(r => r.Id == id);
        if (repair == null) return NotFound();

        if (repair.Status == "Cancelled")
            return BadRequest("Cancelled repair cannot be completed.");

        repair.Status = "Done";
        repair.WorkDone = dto.WorkDone;
        repair.Cost = dto.Cost;
        repair.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }
}