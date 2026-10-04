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
            // Images Unsplash (libres de droits, pas de cle API requise)
            var products = new List<Product>
        {
            new Product {
                Name = "Vélo route RC120", Type = "Bike",
                PriceSale = 799, PriceRental = 25, IsRentable = true,
                // 3 photos pour illustrer la galerie
                ImageUrls = "https://images.unsplash.com/photo-1532298229144-0ec0c57515c7?w=800&q=80,"
                          + "https://images.unsplash.com/photo-1485965120184-e220f721d03e?w=800&q=80,"
                          + "https://images.unsplash.com/photo-1559348349-86f1f65817fe?w=800&q=80"
            },
            new Product {
                Name = "VTT ST 540", Type = "Bike",
                PriceSale = 999, PriceRental = 30, IsRentable = true,
                ImageUrls = "https://images.unsplash.com/photo-1485965120184-e220f721d03e?w=800&q=80"
            },
            new Product {
                Name = "Casque Urbain", Type = "Accessory",
                PriceSale = 49, IsRentable = false,
                ImageUrls = "https://images.unsplash.com/photo-1557687650-5f1e748d8f98?w=800&q=80"
            },
            new Product {
                Name = "Antivol U", Type = "Accessory",
                PriceSale = 35, IsRentable = false,
                ImageUrls = "https://images.unsplash.com/photo-1507035895480-2b3156c31fc8?w=800&q=80"
            }
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

        // ===== ROLES =====
        // On seede les 4 roles metier de l'application.
        // - Admin  : acces complet (back-office + administration)
        // - Manager: gestion d'un magasin (produits, stocks, commandes)
        // - Tech   : atelier / reparations
        // - Client : parcours e-commerce (achat, location, RDV)
        var roleNames = new[] { "Admin", "Manager", "Tech", "Client" };
        foreach (var rn in roleNames)
        {
            if (!await db.Roles.AnyAsync(r => r.Name == rn))
                db.Roles.Add(new Role { Name = rn });
        }
        await db.SaveChangesAsync();

        var adminRole = await db.Roles.FirstAsync(r => r.Name == "Admin");
        var clientRole = await db.Roles.FirstAsync(r => r.Name == "Client");

        // ===== ADMIN USER =====
        if (!await db.Users.AnyAsync(u => u.Email == "admin@velotech.com"))
        {
            db.Users.Add(new User
            {
                Name = "Admin",
                Email = "admin@velotech.com",
                PasswordHash = HashPassword("Admin123!"),
                RoleId = adminRole.Id,
                StoreId = store.Id
            });

            await db.SaveChangesAsync();
        }

        // ===== CLIENT DE DEMO =====
        // Compte utilisateur "final" pour tester le parcours e-commerce
        // (catalogue, panier, checkout, mes commandes, mes locations, etc.).
        if (!await db.Users.AnyAsync(u => u.Email == "client@velotech.com"))
        {
            db.Users.Add(new User
            {
                Name = "Jean Dupont",
                Email = "client@velotech.com",
                PasswordHash = HashPassword("Client123!"),
                RoleId = clientRole.Id,
                StoreId = null // Un client n'est rattache a aucun magasin.
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