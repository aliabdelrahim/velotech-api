using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Velotech.API.Models;

namespace Velotech.API.Data
{
    public class VelotechDbContext : DbContext
    {
        public VelotechDbContext(DbContextOptions<VelotechDbContext> options)
            : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Store> Stores { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<StoreProduct> StoreProducts { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Rental> Rentals { get; set; }
        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<Repair> Repairs { get; set; }
        public DbSet<Payment> Payments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // clé composite StoreProducts
            modelBuilder.Entity<StoreProduct>()
                .HasKey(sp => new { sp.StoreId, sp.ProductId });

            // ===== Global Query Filters (soft delete) =====
            // Toute requete sur ces entites exclut automatiquement les lignes
            // marquees IsDeleted = true. Pour les inclure (ex: back-office),
            // utiliser .IgnoreQueryFilters() sur la requete.
            modelBuilder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);
            modelBuilder.Entity<Store>().HasQueryFilter(s => !s.IsDeleted);

            // ✅ FIX SQL Server: éviter multiple cascade paths
            modelBuilder.Entity<Order>()
                .HasOne(o => o.User)
                .WithMany() // ou .WithMany(u => u.Orders) si tu ajoutes la nav
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Order>()
                .HasOne(o => o.Store)
                .WithMany()
                .HasForeignKey(o => o.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Rental>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Rental>()
                .HasOne(r => r.Store)
                .WithMany()
                .HasForeignKey(r => r.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Rental>()
                .HasOne(r => r.Product)
                .WithMany()
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Store)
                .WithMany()
                .HasForeignKey(a => a.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Product)
                .WithMany()
                .HasForeignKey(a => a.ProductId)
                .OnDelete(DeleteBehavior.NoAction);


            modelBuilder.Entity<Repair>()
                .HasOne(r => r.Store)
                .WithMany()
                .HasForeignKey(r => r.StoreId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Repair>()
                .HasOne(r => r.Product)
                .WithMany()
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Repair>()
                .HasOne(r => r.Technician)
                .WithMany()
                .HasForeignKey(r => r.TechnicianId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Repair>()
                .HasOne(r => r.Appointment)
                .WithMany()
                .HasForeignKey(r => r.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Payment>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.NoAction);

        }

        /// <summary>
        /// Intercepte les suppressions physiques pour les transformer en soft
        /// delete sur les entites qui implementent ISoftDeletable. Les autres
        /// entites sont supprimees normalement (hard delete).
        /// </summary>
        public override int SaveChanges()
        {
            ApplySoftDelete();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplySoftDelete();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplySoftDelete()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is ISoftDeletable && e.State == EntityState.Deleted);

            foreach (EntityEntry entry in entries)
            {
                // On annule la suppression physique...
                entry.State = EntityState.Modified;
                // ...et on marque l'entite comme supprimee.
                var entity = (ISoftDeletable)entry.Entity;
                entity.IsDeleted = true;
                entity.DeletedAt = DateTime.UtcNow;
            }
        }
    }
}
