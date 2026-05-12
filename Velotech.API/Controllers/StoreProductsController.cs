using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StoreProductsController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public StoreProductsController(VelotechDbContext db)
    {
        _db = db;
    }

    // POST: api/storeproducts  (Upsert: crée si n'existe pas, sinon met à jour)
    [HttpPost]
    public async Task<ActionResult<StoreProductDetailsDto>> Upsert(UpsertStoreProductDto dto)
    {
        if (dto.StockSale < 0 || dto.StockRental < 0)
            return BadRequest("Stock values must be >= 0.");

        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == dto.StoreId);
        if (store == null) return NotFound($"Store {dto.StoreId} not found.");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId);
        if (product == null) return NotFound($"Product {dto.ProductId} not found.");

        // règle simple: stock location seulement si rentable
        if (!product.IsRentable && dto.StockRental > 0)
            return BadRequest("StockRental must be 0 for a non-rentable product.");

        var sp = await _db.StoreProducts
            .FirstOrDefaultAsync(x => x.StoreId == dto.StoreId && x.ProductId == dto.ProductId);

        if (sp == null)
        {
            sp = new StoreProduct
            {
                StoreId = dto.StoreId,
                ProductId = dto.ProductId,
                StockSale = dto.StockSale,
                StockRental = dto.StockRental
            };
            _db.StoreProducts.Add(sp);
        }
        else
        {
            sp.StockSale = dto.StockSale;
            sp.StockRental = dto.StockRental;
        }

        await _db.SaveChangesAsync();

        return Ok(new StoreProductDetailsDto
        {
            StoreId = store.Id,
            StoreName = store.Name ?? "",
            ProductId = product.Id,
            ProductName = product.Name ?? "",
            ProductType = product.Type ?? "",
            StockSale = sp.StockSale,
            StockRental = sp.StockRental,
            PriceSale = product.PriceSale
        });
    }

    // GET: api/storeproducts/store/1   (liste stock magasin)
    [HttpGet("store/{storeId:int}")]
    public async Task<ActionResult<List<StoreProductDetailsDto>>> GetByStore(int storeId)
    {
        var storeExists = await _db.Stores.AnyAsync(s => s.Id == storeId);
        if (!storeExists) return NotFound($"Store {storeId} not found.");

        var items = await _db.StoreProducts
            .Where(sp => sp.StoreId == storeId)
            .Include(sp => sp.Product)
            .Include(sp => sp.Store)
            .OrderBy(sp => sp.ProductId)
            .Select(sp => new StoreProductDetailsDto
            {
                StoreId = sp.StoreId,
                StoreName = sp.Store != null ? sp.Store.Name! : "",
                ProductId = sp.ProductId,
                ProductName = sp.Product != null ? sp.Product.Name! : "",
                ProductType = sp.Product != null ? sp.Product.Type! : "",
                StockSale = sp.StockSale,
                StockRental = sp.StockRental,
                PriceSale = sp.Product != null ? sp.Product.PriceSale : 0
            })
            .ToListAsync();

        return Ok(items);
    }

    // GET: api/storeproducts/store/1/product/2  (stock d’un produit dans un magasin)
    [HttpGet("store/{storeId:int}/product/{productId:int}")]
    public async Task<ActionResult<StoreProductDetailsDto>> GetOne(int storeId, int productId)
    {
        var sp = await _db.StoreProducts
            .Include(x => x.Product)
            .Include(x => x.Store)
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.ProductId == productId);

        if (sp == null) return NotFound("StoreProduct not found.");

        return Ok(new StoreProductDetailsDto
        {
            StoreId = sp.StoreId,
            StoreName = sp.Store != null ? sp.Store.Name! : "",
            ProductId = sp.ProductId,
            ProductName = sp.Product != null ? sp.Product.Name! : "",
            ProductType = sp.Product != null ? sp.Product.Type! : "",
            StockSale = sp.StockSale,
            StockRental = sp.StockRental,
            PriceSale = sp.Product != null ? sp.Product.PriceSale : 0
        });
    }

    [HttpDelete("store/{storeId:int}/product/{productId:int}")]
    public async Task<IActionResult> Delete(int storeId, int productId)
    {
        var sp = await _db.StoreProducts
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.ProductId == productId);

        if (sp == null)
            return NotFound();

        _db.StoreProducts.Remove(sp);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}