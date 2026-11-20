using System.ComponentModel.DataAnnotations;

namespace Frontend.Domain.DTOs.Users;

// FORELØBIG: spejler UserService-opgaven. Der ligger endnu ingen godkendt kontrakt i Platform/docs/contracts.
public class LoginRequest
{
    [Required(ErrorMessage = "Brugernavn er påkrævet.")]
    [StringLength(50)]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Adgangskode er påkrævet.")]
    public string Password { get; set; } = string.Empty;
}

// Svar fra login. Token er en JWT med claims "unique_name" og "role".
public record LoginResponse(string Token);

public class CreateUserRequest
{
    [Required(ErrorMessage = "Brugernavn er påkrævet.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Brugernavn skal være 3-50 tegn.")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Adgangskode er påkrævet.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Adgangskode skal være mindst 6 tegn.")]
    public string Password { get; set; } = string.Empty;
}

public record UserDto(Guid Id, string UserName, string Role, DateTime CreatedAt);

public static class Roles
{
    public const string Admin = "Admin";
    public const string Player = "Player";
}
