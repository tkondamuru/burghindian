using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace WebsiteApi.Services;

public static class AuthHelpers
{
    public static bool IsAdminRequest(HttpRequest req)
    {
        var email = GetAuthenticatedEmail(req);
        return IsAdminEmail(email);
    }

    public static bool IsAdminEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var configured = Environment.GetEnvironmentVariable("ADMIN_EMAILS") ?? string.Empty;
        var admins = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.Trim().ToLowerInvariant());

        return admins.Contains(email.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
    }

    public static string GetAuthenticatedEmail(HttpRequest req)
    {
        if (req.Headers.TryGetValue("x-ms-client-principal", out var headerValues))
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(headerValues.ToString()));
                using var document = JsonDocument.Parse(decoded);
                if (document.RootElement.TryGetProperty("claims", out var claims) && claims.ValueKind == JsonValueKind.Array)
                {
                    foreach (var claim in claims.EnumerateArray())
                    {
                        var type = claim.TryGetProperty("typ", out var typProp) ? typProp.GetString() : string.Empty;
                        var value = claim.TryGetProperty("val", out var valProp) ? valProp.GetString() : string.Empty;
                        if (type is "emails" or "email" or "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
                        {
                            return value?.Trim().ToLowerInvariant() ?? string.Empty;
                        }
                    }
                }

                if (document.RootElement.TryGetProperty("userDetails", out var userDetails))
                {
                    return userDetails.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        var emailClaim = req.HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        return emailClaim?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    public static IReadOnlyList<string> GetAssignedRoles(HttpRequest req)
    {
        if (req.Headers.TryGetValue("x-ms-client-principal", out var headerValues))
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(headerValues.ToString()));
                using var document = JsonDocument.Parse(decoded);
                if (document.RootElement.TryGetProperty("userRoles", out var userRoles) &&
                    userRoles.ValueKind == JsonValueKind.Array)
                {
                    return userRoles
                        .EnumerateArray()
                        .Select(role => role.GetString()?.Trim())
                        .Where(role => !string.IsNullOrWhiteSpace(role))
                        .Select(role => role!)
                        .ToArray();
                }
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        // Example usage later:
        // var roles = AuthHelpers.GetAssignedRoles(req);
        // var isAdmin = roles.Contains("admin", StringComparer.OrdinalIgnoreCase);
        return Array.Empty<string>();
    }
}
