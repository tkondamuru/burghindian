using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using WebsiteApi.Services;

namespace WebsiteApi.Functions;

public sealed class AuthFunctions
{
    [Function("GetAuthSession")]
    public IActionResult GetAuthSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/session")] HttpRequest req)
    {
        var email = AuthHelpers.GetAuthenticatedEmail(req);

        return new OkObjectResult(new
        {
            authenticated = !string.IsNullOrWhiteSpace(email),
            email
        });
    }
}
