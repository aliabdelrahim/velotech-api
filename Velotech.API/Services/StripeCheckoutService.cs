using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Velotech.API.Data;
using Velotech.API.Models;

namespace Velotech.API.Services;

/// <summary>
/// Configuration Stripe (section "Stripe" de appsettings / variables d'environnement
/// Stripe__SecretKey, Stripe__WebhookSecret...). Les cles ne doivent JAMAIS etre
/// commitees : en local on utilise "dotnet user-secrets", en prod les App Settings.
/// </summary>
public class StripeSettings
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string Currency { get; set; } = "eur";
}

/// <summary>
/// Paiement en ligne via Stripe Checkout (page de paiement hebergee par Stripe).
///
/// Pourquoi Checkout plutot qu'un formulaire carte maison :
///   - aucune donnee carte ne transite par Velotech (conformite PCI-DSS SAQ A),
///   - 3D Secure / SCA (obligatoire en Europe) gere automatiquement,
///   - Bancontact, cartes, Apple Pay / Google Pay actives depuis le Dashboard
///     Stripe sans changer le code,
///   - page traduite FR / NL.
///
/// Flux :
///   1. le front cree la commande/location puis appelle CreateCheckoutSessionAsync,
///   2. le client est redirige vers Stripe et paie,
///   3. Stripe appelle le webhook (source de verite) -> paiement "Paid",
///   4. la page de retour du front interroge le statut (SyncAsync en secours si
///      le webhook n'est pas encore arrive).
/// </summary>
public class StripeCheckoutService
{
    private readonly VelotechDbContext _db;
    private readonly StripeSettings _settings;
    private readonly ILogger<StripeCheckoutService> _logger;
    private readonly StripeClient? _client;

    public StripeCheckoutService(
        VelotechDbContext db,
        IOptions<StripeSettings> settings,
        ILogger<StripeCheckoutService> logger,
        StripeClient? client = null)
    {
        _db = db;
        _settings = settings.Value;
        _logger = logger;
        _client = client;
    }

    public bool IsConfigured => _client != null;

    private SessionService Sessions =>
        _client?.V1.Checkout.Sessions ?? throw new InvalidOperationException("Stripe:SecretKey manquant.");

    /// <summary>
    /// Cree la Checkout Session Stripe pour un paiement "Pending" deja enregistre.
    /// Les montants viennent exclusivement de la base (jamais du front).
    /// </summary>
    public async Task<Session> CreateSessionAsync(
        Payment payment,
        List<SessionLineItemOptions> lineItems,
        string? customerEmail,
        string successUrl,
        string cancelUrl,
        string? locale)
    {
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            LineItems = lineItems,
            CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail,
            ClientReferenceId = payment.Id.ToString(),
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Locale = locale is "fr" or "nl" or "en" ? locale : "auto",
            // Etiquette pour comparer les parcours dans le Dashboard Stripe.
            IntegrationIdentifier = $"velotech-{payment.PaymentType.ToLowerInvariant()}-{IntegrationSuffix}",
            // Stripe impose au moins 30 min (marge pour la latence / l'horloge).
            // Le stock reserve par la commande est libere a l'expiration
            // (webhook checkout.session.expired).
            ExpiresAt = DateTime.UtcNow.AddMinutes(35),
            Metadata = new Dictionary<string, string>
            {
                ["paymentId"] = payment.Id.ToString(),
                ["paymentType"] = payment.PaymentType,
            },
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Description = $"Velotech - {payment.PaymentType} #{payment.OrderId ?? payment.RentalId ?? payment.RepairId}",
                Metadata = new Dictionary<string, string> { ["paymentId"] = payment.Id.ToString() },
            },
            // Pas de PaymentMethodTypes : Stripe affiche les moyens de paiement
            // actives dans le Dashboard (cartes, Bancontact, Apple Pay...).
        };

        // Pas d'IdempotencyKey basee sur payment.Id : la base locale et la base de
        // prod partagent le meme compte Stripe de test (ids en collision).
        // Stripe.net genere deja une cle unique pour ses propres reessais reseau.
        return await Sessions.CreateAsync(options);
    }

    public Task<Session> GetSessionAsync(string sessionId) => Sessions.GetAsync(sessionId);

    /// <summary>Expire une session encore ouverte (client qui abandonne).</summary>
    public async Task<Session> ExpireSessionAsync(string sessionId) => await Sessions.ExpireAsync(sessionId);

    /// <summary>
    /// Aligne le paiement local sur l'etat de la session Stripe.
    /// Utilise par le webhook ET par la page de retour (idempotent).
    /// </summary>
    public async Task<Payment?> SyncAsync(Session session)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.StripeSessionId == session.Id);
        if (payment == null)
        {
            _logger.LogWarning("Stripe session {SessionId} inconnue en base", session.Id);
            return null;
        }

        // Regle Stripe : livrer des que payment_status n'est pas "unpaid"
        // ("paid" ou "no_payment_required"). Avec un moyen de paiement differe,
        // checkout.session.completed arrive encore "unpaid" : on attend alors
        // checkout.session.async_payment_succeeded.
        if (session.Status == "complete" && session.PaymentStatus != "unpaid")
            await MarkPaidAsync(payment, session.PaymentIntentId);
        else if (session.Status == "expired")
            await MarkFailedAsync(payment);
        // Sinon : session ouverte, ou paiement asynchrone en cours -> on attend.

        return payment;
    }

    public async Task MarkPaidAsync(Payment payment, string? paymentIntentId)
    {
        if (!await TryTransitionAsync(payment, "Paid", paymentIntentId)) return;

        string link = "/dashboard";
        if (payment.OrderId.HasValue)
        {
            var order = await _db.Orders.FirstAsync(o => o.Id == payment.OrderId.Value);
            order.Status = "Paid";
            link = "/my-orders";
        }
        else if (payment.RentalId.HasValue)
        {
            link = "/my-rentals";
        }

        _db.Notifications.Add(new Notification
        {
            UserId = payment.UserId,
            Title = "Paiement recu",
            Message = $"Nous avons bien recu votre paiement de {payment.Amount:0.00} EUR. Merci !",
            Type = "PaymentReceived",
            Link = link,
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("Paiement {PaymentId} confirme par Stripe", payment.Id);
    }

    /// <summary>
    /// Paiement abandonne / expire / refuse. Pour une commande, on l'annule et on
    /// remet le stock reserve en vente (le front recree une commande au prochain essai).
    /// </summary>
    public async Task MarkFailedAsync(Payment payment)
    {
        if (!await TryTransitionAsync(payment, "Failed", null)) return;

        if (payment.OrderId.HasValue)
            await CancelPendingOrderAsync(payment.OrderId.Value);

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Passe le paiement de "Pending" a newStatus de maniere atomique (UPDATE ...
    /// WHERE Status = 'Pending'). Le webhook Stripe et la page de retour / le
    /// bouton annuler peuvent arriver en meme temps : un seul des deux gagne,
    /// ce qui evite de restituer le stock ou de notifier deux fois.
    /// </summary>
    private async Task<bool> TryTransitionAsync(Payment payment, string newStatus, string? paymentIntentId)
    {
        DateTime? paidAt = newStatus == "Paid" ? DateTime.UtcNow : null;

        var updated = await _db.Payments
            .Where(p => p.Id == payment.Id && p.Status == "Pending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, newStatus)
                .SetProperty(p => p.PaidAt, paidAt)
                .SetProperty(p => p.StripePaymentIntentId, p => p.StripePaymentIntentId ?? paymentIntentId));

        // Aligne l'entite suivie par EF sur la base (gagne ou perdu).
        await _db.Entry(payment).ReloadAsync();
        return updated == 1;
    }

    /// <summary>Annule une commande non payee et restitue son stock vente.</summary>
    public async Task CancelPendingOrderAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .FirstAsync(o => o.Id == orderId);

        if (order.Status != "PendingPayment") return;

        order.Status = "Cancelled";

        var productIds = order.OrderItems!.Select(i => i.ProductId).ToList();
        var stocks = await _db.StoreProducts
            .Where(sp => sp.StoreId == order.StoreId && productIds.Contains(sp.ProductId))
            .ToDictionaryAsync(sp => sp.ProductId);

        foreach (var item in order.OrderItems!)
        {
            if (stocks.TryGetValue(item.ProductId, out var sp))
                sp.StockSale += item.Quantity;
        }
    }

    // Suffixe de 8 lettres aleatoires demande par Stripe pour integration_identifier
    // (fixe : il doit rester le meme d'un deploiement a l'autre).
    private const string IntegrationSuffix = "qkvmzrta";

    public static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    public SessionLineItemOptions LineItem(string name, decimal unitPrice, long quantity = 1) => new()
    {
        Quantity = quantity,
        PriceData = new SessionLineItemPriceDataOptions
        {
            Currency = _settings.Currency,
            UnitAmount = ToCents(unitPrice),
            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = name },
        },
    };
}
