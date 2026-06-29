namespace GarionX.Dtos;

public class VerifyOtpRequest
{
    public string Email { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Password { get; set; }
}
