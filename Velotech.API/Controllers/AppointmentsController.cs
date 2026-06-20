using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public AppointmentsController(VelotechDbContext db)
    {
        _db = db;
    }

    // POST: api/appointments
    [HttpPost]
    public async Task<ActionResult<AppointmentDetailsDto>> CreateAppointment(CreateAppointmentDto dto)
    {
        if (dto.ScheduledAt < DateTime.UtcNow)
            return BadRequest("ScheduledAt must be in the future.");

        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == dto.StoreId);
        if (store == null) return NotFound($"Store {dto.StoreId} not found.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId);
        if (user == null) return NotFound($"User {dto.UserId} not found.");

        // Règle métier : user appartient au magasin
        if (user.StoreId != dto.StoreId)
            return BadRequest("User does not belong to this store.");

        Product? product = null;
        if (dto.ProductId.HasValue)
        {
            product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId.Value);
            if (product == null) return NotFound($"Product {dto.ProductId.Value} not found.");
        }

        var serviceType = string.IsNullOrWhiteSpace(dto.ServiceType) ? "Entretien" : dto.ServiceType.Trim();

        var appointment = new Appointment
        {
            UserId = dto.UserId,
            StoreId = dto.StoreId,
            ProductId = dto.ProductId,
            ServiceType = serviceType,
            ScheduledAt = dto.ScheduledAt,
            Notes = dto.Notes,
            Status = "Confirmed",
            CreatedAt = DateTime.UtcNow
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();

        var result = new AppointmentDetailsDto
        {
            AppointmentId = appointment.Id,
            UserId = user.Id,
            UserName = user.Name ?? "",
            StoreId = store.Id,
            StoreName = store.Name ?? "",
            ProductId = appointment.ProductId,
            ProductName = product?.Name ?? "",
            ServiceType = appointment.ServiceType,
            ScheduledAt = appointment.ScheduledAt,
            Status = appointment.Status,
            Notes = appointment.Notes
        };

        return CreatedAtAction(nameof(GetAppointmentById), new { id = appointment.Id }, result);
    }

    // GET: api/appointments/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AppointmentDetailsDto>> GetAppointmentById(int id)
    {
        var appointment = await _db.Appointments
            .Include(a => a.User)
            .Include(a => a.Store)
            .Include(a => a.Product)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (appointment == null) return NotFound();

        var dto = new AppointmentDetailsDto
        {
            AppointmentId = appointment.Id,
            UserId = appointment.UserId,
            UserName = appointment.User?.Name ?? "",
            StoreId = appointment.StoreId,
            StoreName = appointment.Store?.Name ?? "",
            ProductId = appointment.ProductId,
            ProductName = appointment.Product?.Name ?? "",
            ServiceType = appointment.ServiceType,
            ScheduledAt = appointment.ScheduledAt,
            Status = appointment.Status,
            Notes = appointment.Notes
        };

        return Ok(dto);
    }

    // GET: api/appointments?storeId=1&date=2026-02-25
    [HttpGet]
    public async Task<ActionResult<List<AppointmentDetailsDto>>> GetAppointments(
        [FromQuery] int? storeId,
        [FromQuery] DateTime? date,
        [FromQuery] string? status)
    {
        var query = _db.Appointments
            .Include(a => a.User)
            .Include(a => a.Store)
            .Include(a => a.Product)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(a => a.StoreId == storeId.Value);

        if (date.HasValue)
        {
            var d = date.Value.Date;
            var next = d.AddDays(1);
            query = query.Where(a => a.ScheduledAt >= d && a.ScheduledAt < next);
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        var items = await query
            .OrderBy(a => a.ScheduledAt)
            .Select(a => new AppointmentDetailsDto
            {
                AppointmentId = a.Id,
                UserId = a.UserId,
                UserName = a.User != null ? a.User.Name ?? "" : "",
                StoreId = a.StoreId,
                StoreName = a.Store != null ? a.Store.Name ?? "" : "",
                ProductId = a.ProductId,
                ProductName = a.Product != null ? a.Product.Name ?? "" : "",
                ServiceType = a.ServiceType,
                ScheduledAt = a.ScheduledAt,
                Status = a.Status,
                Notes = a.Notes
            })
            .ToListAsync();

        return Ok(items);
    }

    // POST: api/appointments/5/cancel
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelAppointment(int id, CancelAppointmentDto dto)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id);
        if (appointment == null) return NotFound();

        if (appointment.Status == "Cancelled")
            return BadRequest("Appointment is already cancelled.");

        appointment.Status = "Cancelled";
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
