namespace Velotech.API.Dtos;

public class CreateOrderDto
{
    public int UserId { get; set; }
    public int StoreId { get; set; }
    public List<CreateOrderItemDto> Items { get; set; } = new();
}

public class CreateOrderItemDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}