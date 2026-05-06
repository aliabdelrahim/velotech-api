using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public OrdersController(VelotechDbContext db)
    {
        _db = db;
    }

    // POST: api/orders
    [HttpPost]
    public async Task<ActionResult<OrderDetailsDto>> CreateOrder(CreateOrderDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return BadRequest("Order must contain at least one item.");

        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == dto.StoreId);
        if (store == null) return NotFound($"Store {dto.StoreId} not found.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId);
        if (user == null) return NotFound($"User {dto.UserId} not found.");

        // ✅ Règle métier: un employé appartient à un seul magasin
        if (user.StoreId != dto.StoreId)
            return BadRequest("User does not belong to this store.");

        var productIds = dto.Items.Select(i => i.ProductId).Distinct().ToList();

        // Produits existants (catalogue global)
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        if (products.Count != productIds.Count)
            return BadRequest("One or more ProductId are invalid.");

        // ✅ Stock par magasin (StoreProducts)
        var storeStocks = await _db.StoreProducts
            .Where(sp => sp.StoreId == dto.StoreId && productIds.Contains(sp.ProductId))
            .ToDictionaryAsync(sp => sp.ProductId);

        // Vérifie que chaque produit est bien présent dans ce magasin
        foreach (var pid in productIds)
        {
            if (!storeStocks.ContainsKey(pid))
                return BadRequest($"ProductId {pid} is not available in store {dto.StoreId}.");
        }

        // Vérifie stock suffisant (vente)
        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
                return BadRequest("Quantity must be > 0.");

            var sp = storeStocks[item.ProductId];
            if (sp.StockSale < item.Quantity)
                return BadRequest($"Insufficient sale stock for ProductId {item.ProductId}. Available: {sp.StockSale}");
        }

        var order = new Order
        {
            UserId = dto.UserId,
            StoreId = dto.StoreId,
            OrderDate = DateTime.UtcNow,
            TotalAmount = 0m,
            OrderItems = new List<OrderItem>()
        };

        decimal total = 0m;

        foreach (var item in dto.Items)
        {
            var product = products[item.ProductId];
            var unitPrice = product.PriceSale;

            total += unitPrice * item.Quantity;

            order.OrderItems.Add(new OrderItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = unitPrice
            });

            // ✅ décrémente le stock vente du magasin
            storeStocks[item.ProductId].StockSale -= item.Quantity;
        }

        order.TotalAmount = total;

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var details = new OrderDetailsDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            TotalAmount = order.TotalAmount,
            StoreName = store.Name ?? "",
            CustomerName = user.Name ?? "",
            Items = order.OrderItems.Select(oi => new OrderItemDto
            {
                ProductName = products[oi.ProductId].Name ?? $"Product #{oi.ProductId}",
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice
            }).ToList()
        };

        return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, details);
    }

    // GET: api/orders/store/1
    [HttpGet("store/{storeId:int}")]
    public async Task<ActionResult<List<OrderDetailsDto>>> GetOrdersByStore(int storeId)
    {
        var orders = await _db.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Store)
            .Include(o => o.User)
            .Where(o => o.StoreId == storeId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        var productIds = orders
            .SelectMany(o => o.OrderItems)
            .Select(i => i.ProductId)
            .Distinct()
            .ToList();

        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var result = orders.Select(order => new OrderDetailsDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            TotalAmount = order.TotalAmount,
            StoreName = order.Store?.Name ?? "",
            CustomerName = order.User?.Name ?? "",
            Items = order.OrderItems.Select(oi => new OrderItemDto
            {
                ProductName = products.TryGetValue(oi.ProductId, out var p)
                    ? p.Name ?? $"Product #{oi.ProductId}"
                    : $"Product #{oi.ProductId}",
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice
            }).ToList()
        }).ToList();

        return Ok(result);
    }

    // GET: api/orders/5 (détails)
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDetailsDto>> GetOrderById(int id)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Store)
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();

        // Récup produits pour noms
        var productIds = order.OrderItems.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var dto = new OrderDetailsDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            TotalAmount = order.TotalAmount,
            StoreName = order.Store?.Name ?? "",
            CustomerName = order.User?.Name ?? "", // idem ici
            Items = order.OrderItems.Select(oi => new OrderItemDto
            {
                ProductName = products.TryGetValue(oi.ProductId, out var p) ? p.Name : $"Product #{oi.ProductId}",
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice
            }).ToList()
        };

        return Ok(dto);
    }

    // PUT: api/orders/status
    [HttpPut("status")]
    public async Task<IActionResult> UpdateStatus(UpdateOrderDto dto)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == dto.OrderId);
        if (order == null) return NotFound();

        // Exemple simple: statut stocké dans Payments plutôt que Orders,
        // donc ici on ne change rien si ton modèle Order n'a pas Status.
        // Si tu as ajouté Status dans Order, décommente:
        // order.Status = dto.Status;

        await _db.SaveChangesAsync();
        return NoContent();
    }
}