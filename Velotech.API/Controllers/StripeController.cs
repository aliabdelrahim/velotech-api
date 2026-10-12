using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;
using Velotech.API.Services;

namespace Velotech.API.Controllers;

/// <summary>
/// Paiement en ligne Stripe (Checkout + webhook).
/// Partage le prefixe api/payments avec PaymentsController (historique / back-office).
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public class StripeController : ControllerBase
{
    private readonly VelotechDbContext _db;
    private readonly StripeCheckoutService _stripe;
    private readonly StripeSettings _settings;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeController> _logger;

    public StripeController(
        VelotechDbContext db,
        StripeCheckoutService stripe,
        IOptions<StripeSettings> settings,
        IConfiguration config,
        ILogger<StripeController> logger)
    {
        _db = db;
        _stripe = stripe;
        _settings = settings.Value;
        _config = config;
        _logger = logger;
    }

    // POST: api/payments/checkout-session
    // Cree (ou reprend) la session Stripe et renvoie l'URL de paiement.
    [HttpPost("checkout-session")]
    public async Task<ActionResult<CheckoutSessionDto>> CreateCheckoutSession(CreateCheckoutSessionDto dto)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized("User identifier missing from token.");

        if (!_stripe.IsConfigured)
        {
            // Le front vient de creer la commande : on libere son stock.
            if (dto.OrderId.HasValue &&
                await _db.Orders.AnyAsync(o => o.Id == dto.OrderId && o.UserId == userId))
            {
                await _stripe.CancelPendingOrderAsync(dto.OrderId.Value);
                await _db.SaveChangesAsync();
            }
            return StatusCode(503, "Stripe n'est pas configure (Stripe:SecretKey manquant).");
        }

        var targets = new[] { dto.OrderId.HasValue, dto.RentalId.HasValue, dto.RepairId.HasValue }.Count(x => x);
        if (targets != 1)
            return BadRequest("Provide exactly one target: OrderId OR RentalId OR RepairId.");

        var user = await _db.Users.FirstAsync(u => u.Id == userId);

        // ---- 1) Cible + controle de propriete + montant calcule cote serveur ----
        string paymentType;
        decimal amount;
        List<SessionLineItemOptions> lineItems;

        if (dto.OrderId.HasValue)
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems!).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == dto.OrderId.Value);
            if (order == null) return NotFound($"Order {dto.OrderId} not found.");
            if (order.UserId != userId) return Forbid();
            if (order.Status == "Paid") return Conflict("This order is already paid.");
            if (order.Status != "PendingPayment") return Conflict("This order was cancelled. Please place a new order.");

            paymentType = "Order";
            amount = order.TotalAmount;
            lineItems = order.OrderItems!
                .Select(i => _stripe.LineItem(i.Product?.Name ?? $"Produit #{i.ProductId}", i.UnitPrice, i.Quantity))
                .ToList();
            if (order.ShippingFee > 0)
                lineItems.Add(_stripe.LineItem("Livraison a domicile", order.ShippingFee));
        }
        else if (dto.RentalId.HasValue)
        {
            var rental = await _db.Rentals
                .Include(r => r.Product)
                .FirstOrDefaultAsync(r => r.Id == dto.RentalId.Value);
            if (rental == null) return NotFound($"Rental {dto.RentalId} not found.");
            if (rental.UserId != userId) return Forbid();
            if (rental.Status == "Cancelled") return Conflict("This rental was cancelled.");

            paymentType = "Rental";
            amount = rental.TotalPrice;
            var days = Math.Max(1, (rental.EndDate.Date - rental.StartDate.Date).Days);
            lineItems = new()
            {
                _stripe.LineItem(
                    $"Location {rental.Product?.Name} - {days} jour(s) ({rental.StartDate:dd/MM} au {rental.EndDate:dd/MM})",
                    rental.TotalPrice)
            };
        }
        else
        {
            var repair = await _db.Repairs
                .Include(r => r.Appointment)
                .Include(r => r.Product)
                .FirstOrDefaultAsync(r => r.Id == dto.RepairId!.Value);
            if (repair == null) return NotFound($"Repair {dto.RepairId} not found.");
            if (repair.Appointment?.UserId != userId) return Forbid();
            if (repair.Status != "Done" || repair.Cost is not > 0)
                return BadRequest("This repair is not ready to be paid.");

            paymentType = "Repair";
            amount = repair.Cost.Value;
            lineItems = new() { _stripe.LineItem($"Reparation #{repair.Id} - {repair.Product?.Name}", repair.Cost.Value) };
        }

        if (amount <= 0) return BadRequest("Amount must be > 0.");

        var targetPayments = _db.Payments.Where(p =>
            (dto.OrderId.HasValue && p.OrderId == dto.OrderId) ||
            (dto.RentalId.HasValue && p.RentalId == dto.RentalId) ||
            (dto.RepairId.HasValue && p.RepairId == dto.RepairId));

        if (await targetPayments.AnyAsync(p => p.Status == "Paid"))
            return Conflict("This target is already paid.");

        // ---- 2) Reprise d'une session encore ouverte (double clic, retour arriere...) ----
        var pending = await targetPayments
            .Where(p => p.Status == "Pending" && p.StripeSessionId != null)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync();

        if (pending != null)
        {
            var existing = await _stripe.GetSessionAsync(pending.StripeSessionId!);
            if (existing.Status == "open")
                return Ok(new CheckoutSessionDto { PaymentId = pending.Id, SessionId = existing.Id, Url = existing.Url });

            await _stripe.SyncAsync(existing);
            if (pending.Status == "Paid") return Conflict("This target is already paid.");
            if (dto.OrderId.HasValue) return Conflict("This order was cancelled. Please place a new order.");
        }

        // ---- 3) Paiement "Pending" puis session Stripe ----
        var payment = new Payment
        {
            UserId = userId,
            Amount = amount,
            PaymentType = paymentType,
            OrderId = dto.OrderId,
            RentalId = dto.RentalId,
            RepairId = dto.RepairId,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        var frontUrl = (_config["App:FrontendUrl"] ?? "http://localhost:4200").TrimEnd('/');

        try
        {
            var session = await _stripe.CreateSessionAsync(
                payment,
                lineItems,
                user.Email,
                successUrl: $"{frontUrl}/payment/success?session_id={{CHECKOUT_SESSION_ID}}",
                cancelUrl: $"{frontUrl}/payment/cancel?payment_id={payment.Id}",
                locale: dto.Locale);

            payment.StripeSessionId = session.Id;
            await _db.SaveChangesAsync();

            return Ok(new CheckoutSessionDto { PaymentId = payment.Id, SessionId = session.Id, Url = session.Url });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Creation de la session Stripe impossible pour le paiement {PaymentId}", payment.Id);
            await _stripe.MarkFailedAsync(payment);
            return StatusCode(502, "Le service de paiement est indisponible. Reessayez dans quelques instants.");
        }
    }

    // GET: api/payments/checkout-session/{sessionId}
    // Page de retour du front : statut du paiement (resynchronise avec Stripe si
    // le webhook n'est pas encore passe).
    [HttpGet("checkout-session/{sessionId}")]
    public async Task<ActionResult<CheckoutStatusDto>> GetCheckoutStatus(string sessionId)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.StripeSessionId == sessionId);
        if (payment == null) return NotFound();
        if (!CanAccess(payment)) return Forbid();

        if (payment.Status == "Pending" && _stripe.IsConfigured)
        {
            var session = await _stripe.GetSessionAsync(sessionId);
            await _stripe.SyncAsync(session);
        }

        return Ok(ToStatusDto(payment));
    }

    // POST: api/payments/{id}/cancel
    // Le client revient de Stripe sans payer : on expire la session et on libere
    // le stock de la commande.
    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<CheckoutStatusDto>> CancelCheckout(int id)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == id);
        if (payment == null) return NotFound();
        if (!CanAccess(payment)) return Forbid();

        if (payment.Status == "Pending" && payment.StripeSessionId != null)
        {
            var session = await _stripe.GetSessionAsync(payment.StripeSessionId);
            if (session.Status == "open")
                session = await _stripe.ExpireSessionAsync(session.Id);

            // Si le client a finalement paye, SyncAsync le marque "Paid".
            await _stripe.SyncAsync(session);
        }

        return Ok(ToStatusDto(payment));
    }

    // POST: api/payments/webhook
    // Appele par Stripe (pas de JWT) : l'authenticite est verifiee via la
    // signature Stripe-Signature et le secret du endpoint (whsec_...).
    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(_settings.WebhookSecret))
            return BadRequest();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                signature,
                _settings.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Webhook Stripe rejete (signature invalide)");
            return BadRequest();
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
            case EventTypes.CheckoutSessionAsyncPaymentSucceeded:
            case EventTypes.CheckoutSessionExpired:
                await _stripe.SyncAsync((Session)stripeEvent.Data.Object);
                break;

            case EventTypes.CheckoutSessionAsyncPaymentFailed:
                var failed = (Session)stripeEvent.Data.Object;
                var payment = await _db.Payments.FirstOrDefaultAsync(p => p.StripeSessionId == failed.Id);
                if (payment != null) await _stripe.MarkFailedAsync(payment);
                break;
        }

        return Ok();
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private bool CanAccess(Payment payment) =>
        (TryGetUserId(out var userId) && payment.UserId == userId)
        || User.IsInRole("Admin") || User.IsInRole("Manager");

    private static CheckoutStatusDto ToStatusDto(Payment p) => new()
    {
        PaymentId = p.Id,
        PaymentType = p.PaymentType,
        Status = p.Status,
        Amount = p.Amount,
        OrderId = p.OrderId,
        RentalId = p.RentalId,
        RepairId = p.RepairId,
    };
}
