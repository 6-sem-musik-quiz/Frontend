using System.ComponentModel.DataAnnotations;

using Frontend.Domain.Localization;

namespace Frontend.Domain.DTOs.Users;

// Spejler UserService (POST /api/auth/login, /refresh, /logout og POST /api/users),
// kontrakt i Platform/docs/contracts, så UserService's DTO'er er kilden.
public class LoginRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.UserNameRequired))]
    [StringLength(50)]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.PasswordRequired))]
    public string Password { get; set; } = string.Empty;
}

// Svar fra login. AccessToken er en JWT med claims "sub", "unique_name", "role" og "exp" og lever kort
// (15 min). RefreshToken bruges til at få et nyt par uden at logge ind igen.
public record AuthLoginResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User);

// Svar fra refresh: et nyt adgangstoken og et nyt refresh-token. Det gamle refresh-token virker ikke mere.
public record RefreshResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

// Body til /api/auth/refresh og /api/auth/logout.
public record RefreshTokenRequest(string RefreshToken);

public class CreateUserRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.UserNameRequired))]
    [StringLength(50, MinimumLength = 3, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.UserNameLength))]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.PasswordRequired))]
    [StringLength(100, MinimumLength = 6, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.PasswordLength))]
    public string Password { get; set; } = string.Empty;
}

// UserService's offentlige visning af en bruger. Role kommer som tekst ("Player"/"Admin").
public record UserDto(int Id, string UserName, string Role);

public static class Roles
{
    public const string Admin = "Admin";
    public const string Player = "Player";
}
