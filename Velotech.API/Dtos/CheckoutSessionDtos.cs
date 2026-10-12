namespace Velotech.API.Dtos;

/// <summary>Demande de paiement Stripe : exactement une cible.</summary>
public class CreateCheckoutSessionDto
{
    public int? OrderId { get; set; }
    public int? RentalId { get; set; }
    public int? RepairId { get; set; }

    /// <summary>Langue de la page Stripe : "fr", "nl" ou "en" (sinon auto).</summary>
    public string? Locale { get; set; }
}

public class CheckoutSessionDto
{
    public int PaymentId { get; set; }
    public string SessionId { get; set; } = "";
    public string Url { get; set; } = "";
}

public class CheckoutStatusDto
{
    public int PaymentId { get; set; }
    public string PaymentType { get; set; } = "";
    public string Status { get; set; } = ""; // Pending / Paid / Failed
    public decimal Amount { get; set; }
    public int? OrderId { get; set; }
    public int? RentalId { get; set; }
    public int? RepairId { get; set; }
}
