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
            // Location reservee -> confirmee par le paiement
            var confirmed = await _db.Rentals
                .Where(r => r.Id == payment.RentalId.Value && r.Status == "PendingPayment")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "Confirmed"));
            if (confirmed == 0)
                _logger.LogWarning("Paiement {PaymentId} recu pour la location {RentalId} qui n'etait plus en attente (a verifier / rembourser)",
                    payment.Id, payment.RentalId);
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

        // Commande : annulee tout de suite (le front en recree une au prochain essai).
        // Location : elle reste reservee, le client peut retenter depuis "Mes locations" ;
        // la tache de nettoyage la libere si elle n'est toujours pas payee apres 30 min.
        if (payment.OrderId.HasValue)
            await CancelPendingOrderAsync(payment.OrderId.Value);
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

    /// <summary>
    /// Annule une commande non payee et restitue son stock vente.
    /// Atomique (UPDATE ... WHERE Status = 'PendingPayment') : si le webhook et la
    /// tache de nettoyage passent en meme temps, le stock n'est rendu qu'une fois.
    /// </summary>
    public async Task<bool> CancelPendingOrderAsync(int orderId)
    {
        var cancelled = await _db.Orders
            .Where(o => o.Id == orderId && o.Status == "PendingPayment")
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "Cancelled"));
        if (cancelled == 0) return false;

        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.OrderItems)
            .FirstAsync(o => o.Id == orderId);

        foreach (var item in order.OrderItems!)
        {
            await _db.StoreProducts
                .Where(sp => sp.StoreId == order.StoreId && sp.ProductId == item.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(sp => sp.StockSale, sp => sp.StockSale + item.Quantity));
        }
        return true;
    }

    /// <summary>
    /// Annule une location jamais payee et remet le velo en location.
    /// Atomique, et refuse si un paiement Stripe est encore en cours pour elle.
    /// </summary>
    public async Task<bool> CancelPendingRentalAsync(int rentalId)
    {
        var cancelled = await _db.Rentals
            .Where(r => r.Id == rentalId && r.Status == "PendingPayment"
                        && !_db.Payments.Any(p => p.RentalId == r.Id && p.Status == "Pending"))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "Cancelled"));
        if (cancelled == 0) return false;

        var rental = await _db.Rentals.AsNoTracking().FirstAsync(r => r.Id == rentalId);
        await _db.StoreProducts
            .Where(sp => sp.StoreId == rental.StoreId && sp.ProductId == rental.ProductId)
            .ExecuteUpdateAsync(s => s.SetProperty(sp => sp.StockRental, sp => sp.StockRental + 1));
        return true;
    }

    /// <summary>Delai apres lequel une reservation (commande / location) non payee est liberee.</summary>
    public static readonly TimeSpan PaymentHoldDelay = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Nettoyage periodique (PendingPaymentCleanupService) :
    ///  1. paiements Stripe restes "Pending" au-dela de la duree de vie d'une session
    ///     (webhook perdu) : on demande l'etat reel a Stripe ;
    ///  2. locations et commandes non payees depuis plus de 30 min, sans paiement en
    ///     cours : annulees, et le stock est rendu.
    /// </summary>
    public async Task<(int payments, int rentals, int orders)> CleanupAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        int syncedPayments = 0, cancelledRentals = 0, cancelledOrders = 0;

        if (IsConfigured)
        {
            var stalePayments = await _db.Payments
                .Where(p => p.Status == "Pending" && p.StripeSessionId != null
                            && p.CreatedAt < now.AddMinutes(-40))
                .Select(p => p.StripeSessionId!)
                .ToListAsync(ct);

            foreach (var sessionId in stalePayments)
            {
                try
                {
                    await SyncAsync(await GetSessionAsync(sessionId));
                    syncedPayments++;
                }
                catch (StripeException ex)
                {
                    _logger.LogWarning(ex, "Nettoyage : session Stripe {SessionId} illisible", sessionId);
                }
            }
        }

        var holdLimit = now - PaymentHoldDelay;

        var rentalIds = await _db.Rentals
            .Where(r => r.Status == "PendingPayment" && r.CreatedAt < holdLimit)
            .Select(r => r.Id)
            .ToListAsync(ct);
        foreach (var id in rentalIds)
            if (await CancelPendingRentalAsync(id)) cancelledRentals++;

        var orderIds = await _db.Orders
            .Where(o => o.Status == "PendingPayment" && o.OrderDate < holdLimit
                        && !_db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Pending"))
            .Select(o => o.Id)
            .ToListAsync(ct);
        foreach (var id in orderIds)
            if (await CancelPendingOrderAsync(id)) cancelledOrders++;

        return (syncedPayments, cancelledRentals, cancelledOrders);
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
