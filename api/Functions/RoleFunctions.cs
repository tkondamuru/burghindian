using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;

namespace WebsiteApi.Functions;

public sealed class RoleFunctions
{
    [Function("GetRoles")]
    public IActionResult GetRoles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "GetRoles")] HttpRequest req)
    {
        var email = GetEmailFromRoleRequest(req);

        // Placeholder role mapping. Replace this with a table/database lookup when
        // you are ready to manage real application roles by email address.
        var roles = email switch
        {
            "admin@burghindian.com" => new[] { "admin", "editor" },
            "editor@burghindian.com" => new[] { "editor" },
            _ => Array.Empty<string>()
        };

        return new OkObjectResult(new
        {
            roles
        });
    }

    private static string GetEmailFromRoleRequest(HttpRequest req)
    {
        using var document = JsonDocument.Parse(req.Body);
        var root = document.RootElement;

        if (TryGetEmail(root, out var directEmail))
        {
            return directEmail;
        }

        if (root.TryGetProperty("claims", out var claims) && claims.ValueKind == JsonValueKind.Array)
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

        return string.Empty;
    }

    private static bool TryGetEmail(JsonElement root, out string email)
    {
        if (root.TryGetProperty("userDetails", out var userDetails))
        {
            email = userDetails.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(email);
        }

        if (root.TryGetProperty("email", out var directEmail))
        {
            email = directEmail.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(email);
        }

        email = string.Empty;
        return false;
    }
}
