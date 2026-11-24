using System.ComponentModel.DataAnnotations;

namespace Frontend.Domain.DTOs.Users;

// Spejler UserService (POST /api/users/login og POST /api/users). Der ligger endnu ingen godkendt
// kontrakt i Platform/docs/contracts, så UserService's DTO'er er kilden.
public class LoginRequest
{
    [Required(ErrorMessage = "Brugernavn er påkrævet.")]
    [StringLength(50)]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Adgangskode er påkrævet.")]
    public string Password { get; set; } = string.Empty;
}

// Svar fra login. Token er en JWT med claims "sub", "unique_name", "role" og "exp".
public record LoginResponse(string Token, DateTime ExpiresAt, UserDto User);

public class CreateUserRequest
{
    [Required(ErrorMessage = "Brugernavn er påkrævet.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Brugernavn skal være 3-50 tegn.")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Adgangskode er påkrævet.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Adgangskode skal være mindst 6 tegn.")]
    public string Password { get; set; } = string.Empty;
}

// UserService's offentlige visning af en bruger. Role kommer som tekst ("Player"/"Admin").
public record UserDto(int Id, string UserName, string Role);

public static class Roles
{
    public const string Admin = "Admin";
    public const string Player = "Player";
}
