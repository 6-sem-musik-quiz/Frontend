using System.Security.Claims;
using System.Text.Json;

namespace Frontend.Web.Auth;

// Læser claims fra en JWT uden at validere signaturen. Signaturen valideres af backend-services;
// frontenden bruger kun claims til visning og routing.
public static class JwtTokenParser
{
    public const string NameClaimType = "unique_name";
    public const string RoleClaimType = "role";
    public const string AuthenticationType = "jwt";

    // Lange claim-navne, som .NET's JwtSecurityTokenHandler kan udstede, mappes til de korte.
    private static readonly Dictionary<string, string> ClaimTypeAliases = new(StringComparer.Ordinal)
    {
        [ClaimTypes.Role] = RoleClaimType,
        [ClaimTypes.Name] = NameClaimType,
    };

    public static ParsedToken? TryParse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var segments = token.Split('.');
        if (segments.Length != 3)
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(Base64UrlDecode(segments[1]));
            if (payload.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var claims = new List<Claim>();
            DateTimeOffset? expiresAt = null;

            foreach (var property in payload.RootElement.EnumerateObject())
            {
                if (property.Name == "exp" && property.Value.TryGetInt64(out var exp))
                {
                    expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp);
                }

                var type = ClaimTypeAliases.GetValueOrDefault(property.Name, property.Name);
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    claims.AddRange(property.Value.EnumerateArray().Select(v => new Claim(type, ToClaimValue(v))));
                }
                else
                {
                    claims.Add(new Claim(type, ToClaimValue(property.Value)));
                }
            }

            var identity = new ClaimsIdentity(claims, AuthenticationType, NameClaimType, RoleClaimType);
            return new ParsedToken(token, new ClaimsPrincipal(identity), expiresAt);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string ToClaimValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            0 => base64,
            _ => throw new FormatException("Ugyldig base64url."),
        };
        return Convert.FromBase64String(base64);
    }
}

public sealed record ParsedToken(string Token, ClaimsPrincipal Principal, DateTimeOffset? ExpiresAt)
{
    public string? UserName => Principal.Identity?.Name;
    public string? Role => Principal.FindFirst(JwtTokenParser.RoleClaimType)?.Value;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } exp && exp <= now;
}
