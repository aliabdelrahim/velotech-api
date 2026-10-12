namespace Velotech.API.Dtos;

public class CreateOrderDto
{
    public int UserId { get; set; }
    public int StoreId { get; set; }

    // "standard" (livraison a domicile) ou "pickup" (retrait en magasin, gratuit)
    public string ShippingMode { get; set; } = "standard";
    public List<CreateOrderItemDto> Items { get; set; } = new();
}

public class CreateOrderItemDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}