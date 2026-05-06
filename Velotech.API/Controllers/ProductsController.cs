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

    // POST: api/products
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
            IsRentable = dto.IsRentable
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var result = new ProductDetailsDto
        {
            Id = product.Id,
            Name = product.Name ?? "",
            Type = product.Type ?? "",
            PriceSale = product.PriceSale,
            PriceRental = product.PriceRental,
            IsRentable = product.IsRentable
        };

        return CreatedAtAction(nameof(GetProductById), new { id = product.Id }, result);
    }

    // GET: api/products/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDetailsDto>> GetProductById(int id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();

        return Ok(new ProductDetailsDto
        {
            Id = product.Id,
            Name = product.Name ?? "",
            Type = product.Type ?? "",
            PriceSale = product.PriceSale,
            PriceRental = product.PriceRental,
            IsRentable = product.IsRentable
        });
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
                IsRentable = p.IsRentable
            })
            .ToListAsync();

        return Ok(products);
    }

    // PUT: api/products/5
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

        await _db.SaveChangesAsync();

        return Ok(new ProductDetailsDto
        {
            Id = product.Id,
            Name = product.Name ?? "",
            Type = product.Type ?? "",
            PriceSale = product.PriceSale,
            PriceRental = product.PriceRental,
            IsRentable = product.IsRentable
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteProduct(int id)
    {
        var product = await _db.Products.FindAsync(id);

        if (product == null)
            return NotFound();

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}