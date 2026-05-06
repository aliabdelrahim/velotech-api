namespace Velotech.API.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
        public string Email { get; set; } = "";

        public string? PasswordHash { get; set; } = ""; // futur login

        public bool IsActive { get; set; } = true;

        public int RoleId { get; set; }
        public Role? Role { get; set; }

        public int? StoreId { get; set; } = null;
        public Store? Store { get; set; }

    }


}