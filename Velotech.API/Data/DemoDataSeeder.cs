using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Models;

namespace Velotech.API.Data;

/// <summary>
/// Jeu de donnees de demonstration realiste (defense du TFE) : catalogue complet,
/// personnel par magasin, clients, commandes, locations, rendez-vous, reparations,
/// paiements et notifications, etales sur les derniers mois.
///
/// - Execute une seule fois (marqueur : le gerant d'Ixelles de demo).
/// - Les mots de passe des comptes de demo ne sont PAS dans le code (depot public) :
///   ils viennent de la configuration "Seed:DemoPassword" (App Setting Azure
///   Seed__DemoPassword). En developpement local, un mot de passe par defaut est
///   utilise ; en production sans configuration, un mot de passe aleatoire
///   (comptes inutilisables tant que l'admin ne les redefinit pas).
/// - Les e-mails utilisent le domaine reserve example.com : aucun vrai destinataire.
/// </summary>
public static class DemoDataSeeder
{
    private const string MarkerEmail = "sophie.lambert@example.com";
    private const string DevDemoPassword = "Demo2026!";

    private static readonly string[] BrokenOrWrongImages =
    {
        "photo-1557687650-5f1e748d8f98", // lien mort (ancien casque)
    };

    public static async Task SeedAsync(VelotechDbContext db, IConfiguration config, IHostEnvironment env, ILogger logger)
    {
        await FixLegacyProductImagesAsync(db);

        // Mot de passe des comptes de demo defini dans Azure (Seed__DemoPassword) :
        // il est (re)applique a chaque demarrage, ce qui permet de le fixer ou de
        // le changer apres coup sans toucher au code.
        var configuredPassword = config["Seed:DemoPassword"];
        if (await db.Users.AnyAsync(u => u.Email == MarkerEmail))
        {
            if (!string.IsNullOrWhiteSpace(configuredPassword))
                await SyncDemoPasswordsAsync(db, configuredPassword, logger);
            return;
        }

        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            if (env.IsDevelopment())
            {
                password = DevDemoPassword;
            }
            else
            {
                password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                logger.LogWarning("Seed:DemoPassword absent : comptes de demonstration crees avec un mot de passe aleatoire.");
            }
        }

        var now = DateTime.UtcNow;
        var stores = await db.Stores.OrderBy(s => s.Id).ToListAsync();
        if (stores.Count < 3) return;
        var (ixelles, louvain, anvers) = (stores[0], stores[1], stores[2]);

        var roles = await db.Roles.ToDictionaryAsync(r => r.Name!, r => r.Id);

        // ===================== CATALOGUE =====================
        static string Img(params string[] ids) =>
            string.Join(",", ids.Select(id => $"https://images.unsplash.com/photo-{id}?w=800&q=80"));

        var newProducts = new Dictionary<string, Product>
        {
            ["city"] = new() { Name = "Vélo de ville Bruxelles 7V", Type = "Bike", PriceSale = 549, PriceRental = 18, IsRentable = true,
                               ImageUrls = Img("1505705694340-019e1e335916", "1571068316344-75bc76f77890") },
            ["ebike"] = new() { Name = "Vélo électrique E-City 500", Type = "Bike", PriceSale = 1890, PriceRental = 35, IsRentable = true,
                                ImageUrls = Img("1620802051782-725fa33db067") },
            ["mtb"] = new() { Name = "VTT tout suspendu Ardennes FS", Type = "Bike", PriceSale = 1490, PriceRental = 40, IsRentable = true,
                              ImageUrls = Img("1534150034764-046bf225d3fa", "1529422643029-d4585747aaf2") },
            ["gravel"] = new() { Name = "Gravel Explorer GR2", Type = "Bike", PriceSale = 1290, PriceRental = 32, IsRentable = true,
                                 ImageUrls = Img("1576435728678-68d0fbf94e91", "1511994298241-608e28f14fde") },
            ["fixie"] = new() { Name = "Single speed Fixie Noir", Type = "Bike", PriceSale = 429, PriceRental = 15, IsRentable = true,
                                ImageUrls = Img("1593764592116-bfb2a97c642a", "1559348349-86f1f65817fe") },
            ["classic"] = new() { Name = "Vélo vintage Anvers Classic", Type = "Bike", PriceSale = 690, IsRentable = false,
                                  ImageUrls = Img("1571068316344-75bc76f77890") },
            ["carbon"] = new() { Name = "Route carbone Flandrien SL", Type = "Bike", PriceSale = 2490, PriceRental = 45, IsRentable = true,
                                 ImageUrls = Img("1541625602330-2277a4c46182", "1517649763962-0c623066013b") },
            ["light"] = new() { Name = "Éclairage avant LED rechargeable", Type = "Accessory", PriceSale = 29 },
            ["pump"] = new() { Name = "Pompe à pied haute pression", Type = "Accessory", PriceSale = 39 },
            ["bag"] = new() { Name = "Sacoche de porte-bagages 20 L", Type = "Accessory", PriceSale = 59 },
            ["gloves"] = new() { Name = "Gants vélo été", Type = "Accessory", PriceSale = 24 },
            ["tube"] = new() { Name = "Chambre à air 700x28", Type = "Accessory", PriceSale = 8 },
            ["kit"] = new() { Name = "Kit de réparation crevaison", Type = "Accessory", PriceSale = 12 },
        };
        db.Products.AddRange(newProducts.Values);
        await db.SaveChangesAsync();

        // Produits deja presents (seed d'origine)
        var newIds = newProducts.Values.Select(n => n.Id).ToList();
        var existing = await db.Products.Where(p => !newIds.Contains(p.Id)).ToListAsync();
        Product? Find(string name) => existing.FirstOrDefault(p => p.Name == name);
        var P = new Dictionary<string, Product>(newProducts);
        if (Find("Vélo route RC120") is { } rc) P["road"] = rc;
        if (Find("VTT ST 540") is { } vtt) P["vtt"] = vtt;
        if (Find("Casque Urbain") is { } casque) P["helmet"] = casque;
        if (Find("Antivol U") is { } antivol) P["lock"] = antivol;

        // Stocks : chaque magasin a son propre assortiment
        var stock = new (Store store, string key, int sale, int rental)[]
        {
            (ixelles, "city", 6, 3), (ixelles, "ebike", 4, 2), (ixelles, "mtb", 3, 1), (ixelles, "gravel", 2, 1),
            (ixelles, "fixie", 5, 2), (ixelles, "carbon", 2, 1), (ixelles, "light", 25, 0), (ixelles, "pump", 12, 0),
            (ixelles, "bag", 10, 0), (ixelles, "gloves", 18, 0), (ixelles, "tube", 40, 0), (ixelles, "kit", 30, 0),
            (louvain, "city", 8, 4), (louvain, "ebike", 5, 3), (louvain, "gravel", 3, 2), (louvain, "fixie", 4, 2),
            (louvain, "classic", 2, 0), (louvain, "light", 20, 0), (louvain, "pump", 8, 0), (louvain, "tube", 30, 0),
            (louvain, "kit", 15, 0), (louvain, "gloves", 10, 0),
            (anvers, "city", 5, 2), (anvers, "ebike", 6, 3), (anvers, "carbon", 3, 2), (anvers, "gravel", 4, 2),
            (anvers, "classic", 3, 0), (anvers, "light", 15, 0), (anvers, "bag", 8, 0), (anvers, "tube", 35, 0),
            (anvers, "kit", 20, 0),
        };
        foreach (var (st, key, sale, rental) in stock)
            db.StoreProducts.Add(new StoreProduct { StoreId = st.Id, ProductId = P[key].Id, StockSale = sale, StockRental = rental });
        await db.SaveChangesAsync();

        // ===================== PERSONNEL ET CLIENTS =====================
        User NewUser(string name, string email, string role, Store? store) => new()
        {
            Name = name, Email = email, PasswordHash = HashPassword(password!),
            RoleId = roles[role], StoreId = store?.Id, IsActive = true,
        };

        var staff = new List<User>
        {
            NewUser("Sophie Lambert", MarkerEmail, "Manager", ixelles),
            NewUser("Pieter Janssens", "pieter.janssens@example.com", "Manager", louvain),
            NewUser("Lotte Peeters", "lotte.peeters@example.com", "Manager", anvers),
            NewUser("Karim Benali", "karim.benali@example.com", "Tech", ixelles),
            NewUser("Thomas Willems", "thomas.willems@example.com", "Tech", louvain),
            NewUser("Nathalie Dubois", "nathalie.dubois@example.com", "Tech", anvers),
        };
        var clients = new List<User>
        {
            NewUser("Marie Lefèvre", "marie.lefevre@example.com", "Client", null),
            NewUser("Lucas Martin", "lucas.martin@example.com", "Client", null),
            NewUser("Emma Claes", "emma.claes@example.com", "Client", null),
            NewUser("Mehdi Haddad", "mehdi.haddad@example.com", "Client", null),
            NewUser("Sarah Goossens", "sarah.goossens@example.com", "Client", null),
            NewUser("Antoine Mertens", "antoine.mertens@example.com", "Client", null),
            NewUser("Julie Vandenberghe", "julie.vandenberghe@example.com", "Client", null),
            NewUser("Nicolas Renard", "nicolas.renard@example.com", "Client", null),
            NewUser("Laura De Smet", "laura.desmet@example.com", "Client", null),
            NewUser("Hugo Jacobs", "hugo.jacobs@example.com", "Client", null),
        };
        db.Users.AddRange(staff);
        db.Users.AddRange(clients);
        await db.SaveChangesAsync();
        var techByStore = new Dictionary<int, User>
        {
            [ixelles.Id] = staff[3], [louvain.Id] = staff[4], [anvers.Id] = staff[5],
        };

        var notifications = new List<Notification>();
        DateTime Ago(int days, int hour = 10, int minute = 0) =>
            now.Date.AddDays(-days).AddHours(hour).AddMinutes(minute);

        // ===================== COMMANDES + PAIEMENTS =====================
        var orderSpecs = new (int client, Store store, int daysAgo, string mode, string status, (string key, int qty)[] items)[]
        {
            (0, ixelles, 128, "pickup", "Paid", new[] { ("city", 1), ("light", 1) }),
            (1, louvain, 121, "standard", "Paid", new[] { ("helmet", 1), ("gloves", 1) }),
            (2, anvers, 112, "pickup", "Paid", new[] { ("carbon", 1) }),
            (3, ixelles, 104, "standard", "Paid", new[] { ("tube", 2), ("kit", 1) }),
            (4, ixelles, 96, "pickup", "Paid", new[] { ("ebike", 1), ("lock", 1) }),
            (5, louvain, 88, "standard", "Paid", new[] { ("pump", 1) }),
            (6, anvers, 79, "pickup", "Paid", new[] { ("gravel", 1), ("bag", 1) }),
            (7, ixelles, 71, "standard", "Cancelled", new[] { ("fixie", 1) }),
            (8, louvain, 63, "pickup", "Paid", new[] { ("classic", 1) }),
            (9, ixelles, 55, "standard", "Paid", new[] { ("light", 2), ("gloves", 1) }),
            (0, anvers, 47, "standard", "Paid", new[] { ("bag", 1), ("tube", 1) }),
            (2, ixelles, 38, "pickup", "Paid", new[] { ("mtb", 1) }),
            (4, louvain, 29, "standard", "Paid", new[] { ("kit", 2), ("pump", 1) }),
            (6, ixelles, 21, "pickup", "Paid", new[] { ("fixie", 1), ("lock", 1) }),
            (1, anvers, 12, "standard", "Paid", new[] { ("helmet", 1), ("light", 1) }),
            (3, louvain, 6, "pickup", "Paid", new[] { ("ebike", 1) }),
            (5, ixelles, 2, "standard", "Paid", new[] { ("tube", 3), ("kit", 1) }),
        };
        foreach (var (ci, st, daysAgo, mode, status, items) in orderSpecs)
        {
            var lines = items.Where(i => P.ContainsKey(i.key)).ToList();
            if (lines.Count == 0) continue;
            var subtotal = lines.Sum(i => P[i.key].PriceSale * i.qty);
            var shipping = mode == "pickup" || subtotal >= 50m ? 0m : 9.90m;
            var date = Ago(daysAgo, 9 + daysAgo % 9, daysAgo * 7 % 60);
            var order = new Order
            {
                UserId = clients[ci].Id, StoreId = st.Id, OrderDate = date, Status = status,
                ShippingFee = shipping, TotalAmount = subtotal + shipping,
                OrderItems = lines.Select(i => new OrderItem
                    { ProductId = P[i.key].Id, Quantity = i.qty, UnitPrice = P[i.key].PriceSale }).ToList(),
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            db.Payments.Add(new Payment
            {
                UserId = order.UserId, Amount = order.TotalAmount, PaymentType = "Order", OrderId = order.Id,
                Status = status == "Paid" ? "Paid" : "Failed", CreatedAt = date.AddMinutes(1),
                PaidAt = status == "Paid" ? date.AddMinutes(2) : null,
            });
            if (status == "Paid" && daysAgo < 30)
                notifications.Add(new Notification
                {
                    UserId = order.UserId, Title = "Paiement recu", Type = "PaymentReceived", Link = "/my-orders",
                    Message = $"Nous avons bien recu votre paiement de {order.TotalAmount:0.00} EUR. Merci !",
                    CreatedAt = date.AddMinutes(2), IsRead = daysAgo > 7, ReadAt = daysAgo > 7 ? date.AddHours(5) : null,
                });
        }

        // ===================== LOCATIONS + PAIEMENTS =====================
        var rentalSpecs = new (int client, Store store, string key, int startOffset, int days, string status)[]
        {
            (0, ixelles, "city", -110, 3, "Completed"),
            (3, louvain, "ebike", -95, 2, "Completed"),
            (5, anvers, "carbon", -80, 4, "Completed"),
            (7, ixelles, "mtb", -64, 2, "Completed"),
            (9, louvain, "gravel", -50, 5, "Completed"),
            (1, ixelles, "fixie", -33, 1, "Cancelled"),
            (2, anvers, "ebike", -20, 3, "Completed"),
            (4, ixelles, "ebike", -1, 3, "Confirmed"),
            (6, louvain, "city", 4, 2, "Confirmed"),
            (8, anvers, "gravel", 9, 4, "Confirmed"),
            (0, ixelles, "carbon", 16, 2, "Confirmed"),
        };
        foreach (var (ci, st, key, offset, days, status) in rentalSpecs)
        {
            var product = P[key];
            var start = now.Date.AddDays(offset).AddHours(9);
            var created = start.AddDays(-Math.Max(2, days + 3));
            var rental = new Rental
            {
                UserId = clients[ci].Id, StoreId = st.Id, ProductId = product.Id,
                StartDate = start, EndDate = start.AddDays(days),
                TotalPrice = (product.PriceRental ?? 0) * days, Status = status, CreatedAt = created,
            };
            db.Rentals.Add(rental);
            await db.SaveChangesAsync();
            db.Payments.Add(new Payment
            {
                UserId = rental.UserId, Amount = rental.TotalPrice, PaymentType = "Rental", RentalId = rental.Id,
                Status = status == "Cancelled" ? "Failed" : "Paid", CreatedAt = created.AddMinutes(1),
                PaidAt = status == "Cancelled" ? null : created.AddMinutes(2),
            });
        }

        // ===================== RENDEZ-VOUS + REPARATIONS + PAIEMENTS =====================
        var apptSpecs = new (int client, Store store, string key, string service, int dayOffset, int hour, string status,
                             string notes, string? diagnosis, string? workDone, decimal? cost, string? repairStatus)[]
        {
            (1, ixelles, "city", "Révision générale", -100, 10, "Completed", "Révision annuelle avant l'été.",
             "Usure des patins et câbles détendus.", "Remplacement des patins, réglage des freins et des vitesses.", 65m, "Done"),
            (2, louvain, "ebike", "Diagnostic batterie (vélo électrique)", -86, 14, "Completed", "Autonomie en baisse.",
             "Connecteur de batterie oxydé.", "Nettoyage du connecteur et mise à jour du logiciel du moteur.", 45m, "Done"),
            (4, anvers, "carbon", "Réparation dérailleur", -70, 11, "Completed", "Les vitesses sautent.",
             "Patte de dérailleur tordue.", "Redressage de la patte et réglage du dérailleur arrière.", 35m, "Done"),
            (6, ixelles, "mtb", "Réglage suspension", -52, 15, "Completed", "Fourche trop dure.",
             "Pression mal réglée.", "Réglage de la pression et du rebond selon le poids du cycliste.", 30m, "Done"),
            (8, louvain, "gravel", "Réparation pneu / chambre à air", -37, 9, "Completed", "Crevaison arrière.",
             "Chambre à air percée.", "Remplacement de la chambre à air et contrôle du pneu.", 18m, "Done"),
            (3, anvers, "city", "Changement chaîne", -24, 16, "Completed", "Chaîne qui grince.",
             "Chaîne usée à 0,75 %.", "Remplacement de la chaîne et nettoyage de la transmission.", 42m, "Done"),
            (9, ixelles, "ebike", "Réparation freins", -9, 10, "Completed", "Freinage faible.",
             "Plaquettes usées.", "Remplacement des plaquettes avant et arrière, purge des freins.", 55m, "Done"),
            (5, louvain, "fixie", "Centrage roues", -2, 13, "Confirmed", "La roue avant frotte.",
             "Roue avant voilée.", null, null, "InProgress"),
            (0, ixelles, "city", "Pose accessoires", -1, 11, "Confirmed", "Pose d'une sacoche et d'un éclairage.",
             null, null, null, "Open"),
            (7, anvers, "carbon", "Mise au point", 2, 10, "Confirmed", "Préparation d'une sortie longue.", null, null, null, null),
            (2, ixelles, "mtb", "Révision générale", 5, 14, "Pending", "Révision complète avant les Ardennes.", null, null, null, null),
            (4, louvain, "ebike", "Diagnostic batterie (vélo électrique)", 8, 9, "Pending", "Voyant batterie qui clignote.", null, null, null, null),
            (6, anvers, "gravel", "Réparation freins", 11, 15, "Confirmed", "Bruit au freinage.", null, null, null, null),
            (8, ixelles, "fixie", "Changement chaîne", 3, 16, "Cancelled", "Annulé par le client.", null, null, null, null),
        };
        foreach (var a in apptSpecs)
        {
            var when = now.Date.AddDays(a.dayOffset).AddHours(a.hour);
            var appt = new Appointment
            {
                UserId = clients[a.client].Id, StoreId = a.store.Id, ProductId = P[a.key].Id, ServiceType = a.service,
                ScheduledAt = when, Notes = a.notes, Status = a.status, CreatedAt = when.AddDays(-6),
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();

            if (a.repairStatus == null) continue;
            var tech = techByStore[a.store.Id];
            var repair = new Repair
            {
                StoreId = a.store.Id, ProductId = P[a.key].Id, TechnicianId = tech.Id, AppointmentId = appt.Id,
                Diagnosis = a.diagnosis, WorkDone = a.workDone, Status = a.repairStatus, CreatedAt = when,
                StartedAt = a.repairStatus is "InProgress" or "Done" ? when.AddMinutes(20) : null,
                CompletedAt = a.repairStatus == "Done" ? when.AddHours(3) : null, Cost = a.cost,
            };
            db.Repairs.Add(repair);
            await db.SaveChangesAsync();

            if (a.repairStatus == "Done" && a.cost is > 0)
            {
                db.Payments.Add(new Payment
                {
                    UserId = appt.UserId, Amount = a.cost.Value, PaymentType = "Repair", RepairId = repair.Id,
                    Status = "Paid", CreatedAt = when.AddHours(4), PaidAt = when.AddHours(4),
                });
                notifications.Add(new Notification
                {
                    UserId = appt.UserId, Type = "RepairCompleted", Title = "Votre réparation est prête !",
                    Message = $"La réparation de {P[a.key].Name} est terminée. Travail effectué : {a.workDone} Montant : {a.cost:0.00} €.",
                    Link = "/my-appointments", CreatedAt = when.AddHours(3),
                    IsRead = a.dayOffset < -20, ReadAt = a.dayOffset < -20 ? when.AddHours(6) : null,
                });
            }
        }

        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync();
        logger.LogInformation("Donnees de demonstration ajoutees.");
    }

    /// <summary>Corrige les photos du seed d'origine (lien mort, photo ne correspondant pas au produit).</summary>
    private static async Task FixLegacyProductImagesAsync(VelotechDbContext db)
    {
        var products = await db.Products.IgnoreQueryFilters()
            .Where(p => p.Name == "Casque Urbain" || p.Name == "Antivol U" || p.Name == "VTT ST 540")
            .ToListAsync();
        var changed = false;
        foreach (var p in products)
        {
            if (p.ImageUrls == null) continue;
            var dead = BrokenOrWrongImages.Any(id => p.ImageUrls.Contains(id));
            // Antivol et VTT illustres par la photo d'un fixie : on retire / remplace
            var fixieOnAccessory = p.Name == "Antivol U" && p.ImageUrls.Contains("photo-1507035895480-2b3156c31fc8");
            var fixieOnMtb = p.Name == "VTT ST 540" && p.ImageUrls.Contains("photo-1485965120184-e220f721d03e")
                             && !p.ImageUrls.Contains("1529422643029");
            if (dead || fixieOnAccessory)
            {
                p.ImageUrls = null; // le site affiche son illustration par defaut
                changed = true;
            }
            else if (fixieOnMtb)
            {
                p.ImageUrls = "https://images.unsplash.com/photo-1529422643029-d4585747aaf2?w=800&q=80";
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync();
    }

    /// <summary>Applique le mot de passe configure aux comptes de demo (domaine example.com).</summary>
    private static async Task SyncDemoPasswordsAsync(VelotechDbContext db, string password, ILogger logger)
    {
        var demoUsers = await db.Users
            .Where(u => u.Email.EndsWith("@example.com") && !u.IsDeleted)
            .ToListAsync();
        var updated = 0;
        foreach (var user in demoUsers)
        {
            if (user.PasswordHash != null && VerifyPassword(password, user.PasswordHash)) continue;
            user.PasswordHash = HashPassword(password);
            user.FailedLoginCount = 0;
            user.LockoutEndAt = null;
            updated++;
        }
        if (updated > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Mot de passe de demonstration applique a {Count} compte(s).", updated);
        }
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return CryptographicOperations.FixedTimeEquals(pbkdf2.GetBytes(32), expected);
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
