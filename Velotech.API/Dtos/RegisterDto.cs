namespace Velotech.API.Dtos;

public class RegisterDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";

    public int RoleId { get; set; }
    public int StoreId { get; set; }
}