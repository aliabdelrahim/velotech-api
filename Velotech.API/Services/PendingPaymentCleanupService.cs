namespace Velotech.API.Services;

/// <summary>
/// Tache de fond : toutes les 5 minutes, libere les reservations jamais payees
/// (locations et commandes "PendingPayment" de plus de 30 min) et resynchronise
/// avec Stripe les paiements dont le webhook se serait perdu.
///
/// Sur le plan gratuit F1, l'API s'endort apres ~20 min sans trafic : la tache
/// tourne donc pendant que l'API est eveillee, et rattrape tout au reveil.
/// </summary>
public class PendingPaymentCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PendingPaymentCleanupService> _logger;

    public PendingPaymentCleanupService(IServiceScopeFactory scopes, ILogger<PendingPaymentCleanupService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse l'API finir de demarrer (migrations, reveil de la base) avant le 1er passage.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (TaskCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var stripe = scope.ServiceProvider.GetRequiredService<StripeCheckoutService>();
                var (payments, rentals, orders) = await stripe.CleanupAsync(stoppingToken);

                if (payments + rentals + orders > 0)
                    _logger.LogInformation(
                        "Nettoyage : {Payments} paiement(s) resynchronise(s), {Rentals} location(s) et {Orders} commande(s) non payee(s) annulee(s)",
                        payments, rentals, orders);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Une erreur (base en pause, Stripe indisponible...) ne doit jamais arreter la tache.
                _logger.LogError(ex, "Nettoyage des reservations non payees en echec, nouvel essai dans {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
