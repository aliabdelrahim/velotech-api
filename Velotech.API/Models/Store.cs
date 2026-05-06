namespace Velotech.API.Models
{
    public class Store
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public List<StoreProduct>? StoreProducts { get; set; }

        public List<User>? Users { get; set; }
    }
}
