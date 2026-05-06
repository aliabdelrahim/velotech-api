namespace Velotech.API.Dtos;

public class CreateUserDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    public int RoleId { get; set; }
    public int StoreId { get; set; }
}