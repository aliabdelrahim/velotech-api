using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;
using Velotech.API.Dtos;
using Velotech.API.Models;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public ProductsController(VelotechDbContext db)
    {
        _db = db;
    }

    // POST: api/products  (Admin only)
    [Authorize(Roles = "Admin,Manager")]
    [HttpPost]
    public async Task<ActionResult<ProductDetailsDto>> CreateProduct(CreateProductDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");

        if (dto.PriceSale <= 0)
            return BadRequest("PriceSale must be > 0.");

        var type = (dto.Type ?? "").Trim();
        if (!string.Equals(type, "Bike", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, "Accessory", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Type must be 'Bike' or 'Accessory'.");

        // Règles simples
        if (dto.IsRentable)
        {
            if (!string.Equals(type, "Bike", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Only products of type 'Bike' can be rentable.");

            if (dto.PriceRental == null || dto.PriceRental <= 0)
                return BadRequest("PriceRental must be provided (> 0) when IsRentable=true.");
        }
        else
        {
            // si pas rentable, on ignore PriceRental
            dto.PriceRental = null;
        }

        var product = new Product
        {
            Name = dto.Name.Trim(),
            Type = type,
            PriceSale = dto.PriceSale,
            PriceRental = dto.PriceRental,
            IsRentable = dto.IsRentable,
            ImageUrls = string.IsNullOrWhiteSpace(dto.ImageUrls) ? null : dto.ImageUrls.Trim()
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        // Attribution aux magasins avec stocks
        if (dto.StoreStocks != null && dto.StoreStocks.Count > 0)
        {
            var err = await ApplyStoreStocksAsync(product, dto.StoreStocks);
            if (err != null) return BadRequest(err);
        }

        var result = await BuildProductDetailsAsync(product.Id);
        return CreatedAtAction(nameof(GetProductById), new { id = product.Id }, result);
    }

    // GET: api/products/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDetailsDto>> GetProductById(int id)
    {
        var exists = await _db.Products.AnyAsync(p => p.Id == id);
        if (!exists) return NotFound();

        var dto = await BuildProductDetailsAsync(id);
        return Ok(dto);
    }

    // GET: api/products?type=Bike
    [HttpGet]
    public async Task<ActionResult<List<ProductDetailsDto>>> GetProducts([FromQuery] string? type)
    {
        var query = _db.Products.AsQueryable();

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(p => p.Type == type);

        var products = await query
            .OrderBy(p => p.Id)
            .Select(p => new ProductDetailsDto
            {
                Id = p.Id,
                Name = p.Name ?? "",
                Type = p.Type ?? "",
                PriceSale = p.PriceSale,
                PriceRental = p.PriceRental,
                IsRentable = p.IsRentable,
                ImageUrls = p.ImageUrls
            })
            .ToListAsync();

        return Ok(products);
    }

    // PUT: api/products/5  (Admin only)
    [Authorize(Roles = "Admin,Manager")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductDetailsDto>> UpdateProduct(int id, UpdateProductDto dto)
    {
        var product = await _db.Products.FindAsync(id);

        if (product == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");

        if (dto.PriceSale <= 0)
            return BadRequest("PriceSale must be > 0.");

        var type = (dto.Type ?? "").Trim();

        if (!string.Equals(type, "Bike", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, "Accessory", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Type must be 'Bike' or 'Accessory'.");

        if (dto.IsRentable)
        {
            if (!string.Equals(type, "Bike", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Only products of type 'Bike' can be rentable.");

            if (dto.PriceRental == null || dto.PriceRental <= 0)
                return BadRequest("PriceRental must be provided (> 0) when IsRentable=true.");
        }
        else
        {
            dto.PriceRental = null;
        }

        product.Name = dto.Name.Trim();
        product.Type = type;
        product.PriceSale = dto.PriceSale;
        product.PriceRental = dto.PriceRental;
        product.IsRentable = dto.IsRentable;
        product.ImageUrls = string.IsNullOrWhiteSpace(dto.ImageUrls) ? null : dto.ImageUrls.Trim();

        await _db.SaveChangesAsync();

        // Synchronisation des attributions aux magasins (mode "replace")
        if (dto.StoreStocks != null)
        {
            var err = await ApplyStoreStocksAsync(product, dto.StoreStocks);
            if (err != null) return BadRequest(err);
        }

        var result = await BuildProductDetailsAsync(product.Id);
        return Ok(result);
    }

    // ----- Helpers -----

    /// <summary>
    /// Reconstruit un ProductDetailsDto complet avec la liste des stocks par magasin.
    /// </summary>
    private async Task<ProductDetailsDto> BuildProductDetailsAsync(int productId)
    {
        var product = await _db.Products.FirstAsync(p => p.Id == productId);

        var storeStocks = await _db.StoreProducts
            .Where(sp => sp.ProductId == productId)
            .Include(sp => sp.Store)
            .Select(sp => new ProductStoreStockDto
            {
                StoreId = sp.StoreId,
                StoreName = sp.Store != null ? sp.Store.Name : null,
                StockSale = sp.StockSale,
                StockRental = sp.StockRental
            })
            .ToListAsync();

        return new ProductDetailsDto
        {
            Id = product.Id,
            Name = product.Name ?? "",
            Type = product.Type ?? "",
            PriceSale = product.PriceSale,
            PriceRental = product.PriceRental,
            IsRentable = product.IsRentable,
            ImageUrls = product.ImageUrls,
            StoreStocks = storeStocks
        };
    }

    /// <summary>
    /// Applique la liste des stocks fournie en mode "replace" :
    ///  - ajoute les nouvelles associations (StoreId pas encore present)
    ///  - met a jour les quantites existantes
    ///  - supprime les associations qui ne sont plus dans la liste
    /// Valide aussi que les StoreId existent et que StockRental=0 si !IsRentable.
    /// Retourne un message d'erreur ou null si tout est OK.
    /// </summary>
    private async Task<string?> ApplyStoreStocksAsync(Product product, List<ProductStoreStockDto> stocks)
    {
        // Validation : StoreIds existants
        var storeIds = stocks.Select(s => s.StoreId).Distinct().ToList();
        var existingStoreIds = await _db.Stores
            .Where(s => storeIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync();

        var missing = storeIds.Except(existingStoreIds).ToList();
        if (missing.Count > 0)
            return $"Store(s) not found: {string.Join(", ", missing)}";

        // Validation : stocks >= 0 + coherence rentable
        foreach (var s in stocks)
        {
            if (s.StockSale < 0 || s.StockRental < 0)
                return "Stock values must be >= 0.";
            if (!product.IsRentable && s.StockRental > 0)
                return "StockRental must be 0 for a non-rentable product.";
        }

        // Recupere les StoreProducts actuels pour ce produit
        var current = await _db.StoreProducts
            .Where(sp => sp.ProductId == product.Id)
            .ToListAsync();

        // 1) Suppression des associations qui ne sont plus dans la liste
        var newStoreIds = stocks.Select(s => s.StoreId).ToHashSet();
        var toRemove = current.Where(sp => !newStoreIds.Contains(sp.StoreId)).ToList();
        _db.StoreProducts.RemoveRange(toRemove);

        // 2) Upsert : update si existe, insert sinon
        foreach (var s in stocks)
        {
            var existing = current.FirstOrDefault(sp => sp.StoreId == s.StoreId);
            if (existing != null)
            {
                existing.StockSale = s.StockSale;
                existing.StockRental = s.StockRental;
            }
            else
            {
                _db.StoreProducts.Add(new Models.StoreProduct
                {
                    StoreId = s.StoreId,
                    ProductId = product.Id,
                    StockSale = s.StockSale,
                    StockRental = s.StockRental
                });
            }
        }

        await _db.SaveChangesAsync();
        return null;
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteProduct(int id)
    {
        var product = await _db.Products.FindAsync(id);

        if (product == null)
            return NotFound();

        // Soft delete : grace a l'override SaveChanges dans VelotechDbContext,
        // ce Remove() ne supprime pas physiquement la ligne mais la marque
        // comme supprimee (IsDeleted = true, DeletedAt = now). Les commandes
        // et locations historiques qui pointent vers ce produit continuent
        // de fonctionner. Utiliser le endpoint POST /{id}/restore pour annuler.
        _db.Products.Remove(product);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Restaure un produit precedemment soft-deleted.
    /// Utilise IgnoreQueryFilters() pour voir les produits supprimes
    /// (le Global Query Filter les masque par defaut).
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult> RestoreProduct(int id)
    {
        var product = await _db.Products
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null) return NotFound();
        if (!product.IsDeleted) return BadRequest("Product is not deleted.");

        product.IsDeleted = false;
        product.DeletedAt = null;
        await _db.SaveChangesAsync();

        return NoContent();
    }
}