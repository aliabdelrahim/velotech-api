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
                StockRental = sp.StockRental,
                ImageUrls = sp.Product.ImageUrls
            })
            .ToListAsync();

        return Ok(result);
    }

    /// <summary>
    /// Liste les magasins dans lesquels un produit est disponible (stock > 0).
    /// Utilise par la fiche produit pour afficher "Disponible dans ces magasins".
    /// Endpoint public (le visiteur doit pouvoir le voir avant de se connecter).
    /// </summary>
    [HttpGet("product/{productId:int}/stores")]
    public async Task<ActionResult<List<StoreAvailabilityDto>>> GetStoresForProduct(int productId)
    {
        var exists = await _db.Products.AnyAsync(p => p.Id == productId);
        if (!exists) return NotFound($"Product {productId} not found.");

        var result = await _db.StoreProducts
            .Where(sp => sp.ProductId == productId && (sp.StockSale > 0 || sp.StockRental > 0))
            .Include(sp => sp.Store)
            .Select(sp => new StoreAvailabilityDto
            {
                StoreId = sp.StoreId,
                StoreName = sp.Store!.Name,
                Address = sp.Store.Address,
                StockSale = sp.StockSale,
                StockRental = sp.StockRental
            })
            .ToListAsync();

        return Ok(result);
    }
}
