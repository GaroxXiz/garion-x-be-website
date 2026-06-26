namespace GarionX.Dtos;

public class GoogleLoginRequest
{
    public string IdToken { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
}
