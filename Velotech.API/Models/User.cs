namespace Velotech.API.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
        public string Email { get; set; } = "";

        public string? PasswordHash { get; set; } = ""; // futur login

        // Token de reset de mot de passe (genere par /auth/forgot-password)
        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetExpiresAt { get; set; }

        public bool IsActive { get; set; } = true;

        // Soft delete : la ligne reste en base pour preserver l'historique
        // (commandes, locations, paiements) mais le compte est marque comme
        // supprime. Les donnees personnelles sont anonymisees au moment de
        // la desinscription (RGPD).
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }

        public int RoleId { get; set; }
        public Role? Role { get; set; }

        public int? StoreId { get; set; } = null;
        public Store? Store { get; set; }

    }


}