namespace Velotech.API.Models;

public class Order
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }

    // Frais de livraison inclus dans TotalAmount (0 si retrait en magasin
    // ou si le sous-total depasse le seuil de livraison gratuite).
    public decimal ShippingFee { get; set; }

    // "PendingPayment", "Paid", "Cancelled"
    // Une commande est creee en PendingPayment puis passe a Paid quand Stripe
    // confirme le paiement (webhook). Si le paiement est abandonne, elle passe
    // a Cancelled et le stock reserve est remis en vente.
    public string Status { get; set; } = "PendingPayment";

    public List<OrderItem>? OrderItems { get; set; }
}
