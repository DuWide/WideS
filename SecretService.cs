using System.Security.Cryptography;
using System.Text;

namespace DevCockpit;

public static class SecretService
{
    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
        {
            return "";
        }

        try
        {
            var bytes = Convert.FromBase64String(encrypted);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return "";
        }
    }

    private const string HashScheme = "pbkdf2-sha256";
    private const int HashIterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    /// <summary>
    /// Необратимый хэш пароля входа (PBKDF2-SHA256 с солью). Расшифровать его нельзя,
    /// в отличие от DPAPI, — можно только проверить введённый пароль.
    /// </summary>
    public static string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "";
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, HashIterations, HashAlgorithmName.SHA256, HashSize);
        return $"{HashScheme}${HashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPasswordHash(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != HashScheme || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? ""), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Задать новый пароль входа: сохраняется только хэш, старое DPAPI-поле очищается.</summary>
    public static void SetLoginPassword(AppSettingsData settings, string password)
    {
        settings.LoginPasswordHash = HashPassword(password);
        settings.LoginPasswordEncrypted = "";
    }

    /// <summary>
    /// Проверка пароля входа. Если пароль ещё хранится по-старому (DPAPI), после успешного входа
    /// он прозрачно переводится на хэш (<paramref name="upgraded"/> = true — настройки нужно сохранить).
    /// Поведение для старого формата не меняется, чтобы никого не заблокировать.
    /// </summary>
    public static bool VerifyLogin(AppSettingsData settings, string input, out bool upgraded)
    {
        upgraded = false;
        if (!string.IsNullOrEmpty(settings.LoginPasswordHash))
        {
            return VerifyPasswordHash(input, settings.LoginPasswordHash);
        }

        var expected = Unprotect(settings.LoginPasswordEncrypted);
        if (string.IsNullOrEmpty(expected))
        {
            return true;
        }

        if (input != expected)
        {
            return false;
        }

        SetLoginPassword(settings, input);
        upgraded = true;
        return true;
    }
}
