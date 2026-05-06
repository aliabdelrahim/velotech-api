namespace Velotech.API.Dtos;

public class UserDetailsDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";

    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";
}