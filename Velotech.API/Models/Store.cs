namespace Velotech.API.Models
{
    public class Store : ISoftDeletable
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public List<StoreProduct>? StoreProducts { get; set; }

        public List<User>? Users { get; set; }

        // Soft delete : la ligne reste en base, mais est exclue des requetes.
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
    }
}
