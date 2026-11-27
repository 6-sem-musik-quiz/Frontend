namespace Frontend.Domain.Localization;

// Validation messages for the DTOs' DataAnnotations (ErrorMessageResourceType), in the current language.
public static class ValidationText
{
    public static string UserNameRequired => Lang.T("Brugernavn er påkrævet.", "Username is required.");
    public static string UserNameLength => Lang.T("Brugernavn skal være 3-50 tegn.", "Username must be 3-50 characters.");
    public static string PasswordRequired => Lang.T("Adgangskode er påkrævet.", "Password is required.");
    public static string PasswordLength => Lang.T("Adgangskode skal være mindst 6 tegn.", "Password must be at least 6 characters.");
}
