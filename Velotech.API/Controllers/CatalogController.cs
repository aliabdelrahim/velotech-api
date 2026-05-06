using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;
using Velotech.API.Dtos;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CatalogController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public CatalogController(VelotechDbContext db)
    {
        _db = db;
    }

    // ✅ GET api/catalog/store/1
    [HttpGet("store/{storeId:int}")]
    public async Task<ActionResult<List<ProductCatalogDto>>> GetCatalogByStore(int storeId)
    {
        var exists = await _db.Stores.AnyAsync(s => s.Id == storeId);
        if (!exists) return NotFound($"Store {storeId} not found.");

        var result = await _db.StoreProducts
            .Where(sp => sp.StoreId == storeId)
            .Include(sp => sp.Product)
            .Select(sp => new ProductCatalogDto
            {
                ProductId = sp.ProductId,
                Name = sp.Product!.Name,
                Type = sp.Product.Type,
                PriceSale = sp.Product.PriceSale,
                PriceRental = sp.Product.PriceRental,
                IsRentable = sp.Product.IsRentable,
                StockSale = sp.StockSale,
                StockRental = sp.StockRental
            })
            .ToListAsync();

        return Ok(result);
    }
}
