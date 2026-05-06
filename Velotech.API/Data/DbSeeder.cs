using Velotech.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System;

namespace Velotech.API.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(VelotechDbContext db)
    {
        await db.Database.MigrateAsync();

        // ===== STORE =====
        if (!await db.Stores.AnyAsync())
        {
            db.Stores.Add(new Store
            {
                Name = "Velotech Ixelles",
                Address = "Ixelles, Bruxelles"
            });
            await db.SaveChangesAsync();
        }

        var store = await db.Stores.FirstAsync();

        // ===== PRODUCTS =====
        if (!await db.Products.AnyAsync())
        {
            var products = new List<Product>
        {
            new Product { Name = "Vélo route RC120", Type = "Bike", PriceSale = 799, PriceRental = 25, IsRentable = true },
            new Product { Name = "VTT ST 540", Type = "Bike", PriceSale = 999, PriceRental = 30, IsRentable = true },
            new Product { Name = "Casque Urbain", Type = "Accessory", PriceSale = 49, IsRentable = false },
            new Product { Name = "Antivol U", Type = "Accessory", PriceSale = 35, IsRentable = false }
        };

            db.Products.AddRange(products);
            await db.SaveChangesAsync();

            db.StoreProducts.AddRange(new List<StoreProduct>
        {
            new StoreProduct { StoreId = store.Id, ProductId = products[0].Id, StockSale = 5, StockRental = 2 },
            new StoreProduct { StoreId = store.Id, ProductId = products[1].Id, StockSale = 3, StockRental = 1 },
            new StoreProduct { StoreId = store.Id, ProductId = products[2].Id, StockSale = 20, StockRental = 0 },
            new StoreProduct { StoreId = store.Id, ProductId = products[3].Id, StockSale = 15, StockRental = 0 },
        });

            await db.SaveChangesAsync();
        }

        // ===== ROLE =====
        if (!await db.Roles.AnyAsync(r => r.Name == "Admin"))
        {
            db.Roles.Add(new Role { Name = "Admin" });
            await db.SaveChangesAsync();
        }

        var role = await db.Roles.FirstAsync(r => r.Name == "Admin");

        // ===== ADMIN USER =====
        if (!await db.Users.AnyAsync(u => u.Email == "admin@velotech.com"))
        {
            db.Users.Add(new User
            {
                Name = "Admin",
                Email = "admin@velotech.com",
                PasswordHash = HashPassword("Admin123!"),
                RoleId = role.Id,
                StoreId = store.Id
            });

            await db.SaveChangesAsync();
        }
    }
    

private static string HashPassword(string password)
{
    const int iterations = 100_000;
    byte[] salt = RandomNumberGenerator.GetBytes(16);

    using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
    byte[] hash = pbkdf2.GetBytes(32);

    return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
}

}