using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Default : tout user connecte. Les methodes staff-only ont leur propre attribut.
public class RepairsController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public RepairsController(VelotechDbContext db)
    {
        _db = db;
    }

    // POST: api/repairs  (staff only : creation d'une reparation par un tech/manager/admin)
    [Authorize(Roles = "Admin,Manager,Tech,Technicien")]
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

        // Verification que le user assigne est bien un technicien.
        // Les roles seedes sont : Admin / Manager / Tech / Client.
        // On accepte "Tech" (nom seede) ET "Technicien" (variante FR) par securite.
        if (tech.Role != null)
        {
            var rn = tech.Role.Name ?? "";
            if (!string.Equals(rn, "Tech", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(rn, "Technicien", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest($"L'utilisateur selectionne n'est pas un technicien (role actuel : {rn}).");
            }
        }

        Appointment? appointment = null;
        if (dto.AppointmentId.HasValue)
        {
            appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == dto.AppointmentId.Value);
            if (appointment == null) return NotFound($"Appointment {dto.AppointmentId.Value} not found.");

            if (appointment.StoreId != dto.StoreId)
                return BadRequest("Appointment does not belong to this store.");
        }

        var now = DateTime.UtcNow;
        var repair = new Repair
        {
            StoreId = dto.StoreId,
            ProductId = dto.ProductId,
            TechnicianId = dto.TechnicianId,
            AppointmentId = dto.AppointmentId,
            Diagnosis = dto.Diagnosis,
            // Si AutoStart=true (defaut), la reparation est demarree directement
            // (statut InProgress + StartedAt renseigne). Simplifie le workflow du tech.
            Status = dto.AutoStart ? "InProgress" : "Open",
            CreatedAt = now,
            StartedAt = dto.AutoStart ? now : null
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

    /// <summary>
    /// GET: api/repairs/by-appointment/{appointmentId}
    /// Retourne la reparation liee a un RDV donne.
    /// Un client authentifie ne peut voir que les reparations de SES propres RDV.
    /// </summary>
    [HttpGet("by-appointment/{appointmentId:int}")]
    public async Task<ActionResult<RepairDetailsDto>> GetByAppointment(int appointmentId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (appointment == null) return NotFound("Rendez-vous introuvable.");

        // Securite : un client ne peut voir que ses propres reparations.
        // Admin/Manager/Tech peuvent voir celles de leur magasin via l'endpoint classique.
        var isOwnAppointment = appointment.UserId == userId;
        var roleName = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var isStaff = roleName == "Admin" || roleName == "Manager"
                   || roleName == "Tech" || roleName == "Technicien";
        if (!isOwnAppointment && !isStaff)
            return Forbid();

        var repair = await _db.Repairs
            .Include(r => r.Store)
            .Include(r => r.Product)
            .Include(r => r.Technician)
            .FirstOrDefaultAsync(r => r.AppointmentId == appointmentId);

        if (repair == null)
            return NotFound("Aucune reparation liee a ce rendez-vous.");

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

    // GET: api/repairs/mine  (le technicien connecte voit SES reparations)
    [Authorize(Roles = "Admin,Manager,Tech,Technicien")]
    [HttpGet("mine")]
    public async Task<ActionResult<List<RepairDetailsDto>>> GetMyRepairs()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
            return Unauthorized("User identifier missing from token.");

        var items = await _db.Repairs
            .Include(r => r.Store)
            .Include(r => r.Product)
            .Include(r => r.Technician)
            .Where(r => r.TechnicianId == userId)
            .OrderByDescending(r => r.CreatedAt)
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

    // GET: api/repairs/5  (staff only)
    [Authorize(Roles = "Admin,Manager,Tech,Technicien")]
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

    // GET: api/repairs?storeId=1&status=Open  (staff only)
    [Authorize(Roles = "Admin,Manager,Tech,Technicien")]
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

    // POST: api/repairs/5/start  (staff only)
    [Authorize(Roles = "Admin,Manager,Tech,Technicien")]
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

    // POST: api/repairs/5/complete  (staff only)
    [Authorize(Roles = "Admin,Manager,Tech,Technicien")]
    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> CompleteRepair(int id, CompleteRepairDto dto)
    {
        // On charge aussi l'Appointment lie pour pouvoir prevenir le client
        // (c'est lui qui a reserve le RDV).
        var repair = await _db.Repairs
            .Include(r => r.Product)
            .Include(r => r.Appointment)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (repair == null) return NotFound();

        if (repair.Status == "Cancelled")
            return BadRequest("Cancelled repair cannot be completed.");

        repair.Status = "Done";
        repair.WorkDone = dto.WorkDone;
        repair.Cost = dto.Cost;
        repair.CompletedAt = DateTime.UtcNow;

        // ===== Mise a jour du RDV lie =====
        // Quand la reparation est terminee, le RDV atelier est aussi "clos" :
        // le client est passe, le vélo est répare. On met le RDV en "Completed"
        // pour qu'il ne reste pas "A venir" dans l'espace client.
        if (repair.Appointment != null)
        {
            repair.Appointment.Status = "Completed";
        }

        // ===== Notification au client =====
        // Si la reparation est liee a un RDV, on previent le client qui l'a pose.
        if (repair.Appointment != null)
        {
            var clientId = repair.Appointment.UserId;
            var bikeName = repair.Product?.Name ?? "votre vélo";
            // Cost est decimal? -> unwrap avant formatage
            var costStr = (dto.Cost ?? 0m).ToString("0.##");

            _db.Notifications.Add(new Models.Notification
            {
                UserId = clientId,
                Type = "RepairCompleted",
                Title = "Votre réparation est prête !",
                Message = $"La réparation de {bikeName} est terminée. " +
                          $"Travail effectué : {dto.WorkDone}. Montant à régler : {costStr} €.",
                Link = "/my-appointments",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}