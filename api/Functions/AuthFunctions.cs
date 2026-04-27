using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using WebsiteApi.Services;

namespace WebsiteApi.Functions;

public sealed class AuthFunctions
{
    private readonly ILogger<AuthFunctions> _logger;

    public AuthFunctions(ILogger<AuthFunctions> logger)
    {
        _logger = logger;
    }

    [Function("GetAuthSession")]
    public IActionResult GetAuthSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/session")] HttpRequest req)
    {
        try
        {
            var email = AuthHelpers.GetAuthenticatedEmail(req);
            var isAdmin = AuthHelpers.IsAdminEmail(email);

            return new OkObjectResult(new
            {
                authenticated = !string.IsNullOrWhiteSpace(email),
                email,
                isAdmin
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auth session lookup failed for request path {Path}.", req.Path.Value);
            return new ObjectResult(new { error = "Unexpected server error." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
