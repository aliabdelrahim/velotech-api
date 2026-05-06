using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public PaymentsController(VelotechDbContext db)
    {
        _db = db;
    }

    // POST: api/payments  (paiement simulé)
    [HttpPost]
    public async Task<ActionResult<PaymentDetailsDto>> CreatePayment(CreatePaymentDto dto)
    {
        if (dto.Amount <= 0)
            return BadRequest("Amount must be > 0.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId);
        if (user == null) return NotFound($"User {dto.UserId} not found.");

        // ✅ Une seule cible
        var targets = new[] { dto.OrderId.HasValue, dto.RentalId.HasValue, dto.RepairId.HasValue }
            .Count(x => x);

        if (targets != 1)
            return BadRequest("Provide exactly one target: OrderId OR RentalId OR RepairId.");

        string paymentType;
        int? orderId = null;
        int? rentalId = null;
        int? repairId = null;

        // Vérifier existence cible + (optionnel) cohérence montant
        if (dto.OrderId.HasValue)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == dto.OrderId.Value);
            if (order == null) return NotFound($"Order {dto.OrderId.Value} not found.");

            paymentType = "Order";
            orderId = order.Id;

            // optionnel: vérifier que montant correspond
            // if (dto.Amount != order.TotalAmount) return BadRequest("Amount must match order total.");
        }
        else if (dto.RentalId.HasValue)
        {
            var rental = await _db.Rentals.FirstOrDefaultAsync(r => r.Id == dto.RentalId.Value);
            if (rental == null) return NotFound($"Rental {dto.RentalId.Value} not found.");

            paymentType = "Rental";
            rentalId = rental.Id;
        }
        else
        {
            var repair = await _db.Repairs.FirstOrDefaultAsync(r => r.Id == dto.RepairId.Value);
            if (repair == null) return NotFound($"Repair {dto.RepairId.Value} not found.");

            paymentType = "Repair";
            repairId = repair.Id;
        }

        // (Optionnel) éviter double paiement sur la même cible
        var alreadyPaid = await _db.Payments.AnyAsync(p =>
            p.Status == "Paid" &&
            ((orderId.HasValue && p.OrderId == orderId) ||
             (rentalId.HasValue && p.RentalId == rentalId) ||
             (repairId.HasValue && p.RepairId == repairId)));

        if (alreadyPaid)
            return BadRequest("This target is already paid.");

        var payment = new Payment
        {
            UserId = user.Id,
            Amount = dto.Amount,
            PaymentType = paymentType,
            OrderId = orderId,
            RentalId = rentalId,
            RepairId = repairId,
            Status = "Paid",
            CreatedAt = DateTime.UtcNow
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        var result = new PaymentDetailsDto
        {
            PaymentId = payment.Id,
            UserId = user.Id,
            UserName = user.Name ?? "",
            PaymentType = payment.PaymentType,
            Amount = payment.Amount,
            Status = payment.Status,
            OrderId = payment.OrderId,
            RentalId = payment.RentalId,
            RepairId = payment.RepairId,
            CreatedAt = payment.CreatedAt
        };

        return CreatedAtAction(nameof(GetPaymentById), new { id = payment.Id }, result);
    }

    // GET: api/payments/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PaymentDetailsDto>> GetPaymentById(int id)
    {
        var payment = await _db.Payments
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (payment == null) return NotFound();

        return Ok(new PaymentDetailsDto
        {
            PaymentId = payment.Id,
            UserId = payment.UserId,
            UserName = payment.User?.Name ?? "",
            PaymentType = payment.PaymentType,
            Amount = payment.Amount,
            Status = payment.Status,
            OrderId = payment.OrderId,
            RentalId = payment.RentalId,
            RepairId = payment.RepairId,
            CreatedAt = payment.CreatedAt
        });
    }

    // GET: api/payments?userId=1&type=Order
    [HttpGet]
    public async Task<ActionResult<List<PaymentDetailsDto>>> GetPayments([FromQuery] int? userId, [FromQuery] string? type)
    {
        var query = _db.Payments
            .Include(p => p.User)
            .AsQueryable();

        if (userId.HasValue)
            query = query.Where(p => p.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(p => p.PaymentType == type);

        var items = await query
            .OrderByDescending(p => p.Id)
            .Select(p => new PaymentDetailsDto
            {
                PaymentId = p.Id,
                UserId = p.UserId,
                UserName = p.User != null ? p.User.Name ?? "" : "",
                PaymentType = p.PaymentType,
                Amount = p.Amount,
                Status = p.Status,
                OrderId = p.OrderId,
                RentalId = p.RentalId,
                RepairId = p.RepairId,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }
}