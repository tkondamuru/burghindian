using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace WebsiteApi.Services;

public static class AuthHelpers
{
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
}
