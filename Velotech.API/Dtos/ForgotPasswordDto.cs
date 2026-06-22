namespace Velotech.API.Dtos;

public class ForgotPasswordDto
{
    public string Email { get; set; } = "";
}

public class ResetPasswordDto
{
    public string Token { get; set; } = "";
    public string NewPassword { get; set; } = "";
}
