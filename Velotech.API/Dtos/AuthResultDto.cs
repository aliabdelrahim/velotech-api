namespace Velotech.API.Dtos;

public class AuthResultDto
{
    public string Token { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public int UserId { get; set; }
    public string Role { get; set; } = "";
    public int? StoreId { get; set; } = null;
}