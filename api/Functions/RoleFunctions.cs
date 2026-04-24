using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace WebsiteApi.Functions;

public sealed class RoleFunctions
{
    [Function("GetRoles")]
    public IActionResult GetRoles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "GetRoles")] HttpRequest req)
    {
        return new OkObjectResult(new
        {
            roles = Array.Empty<string>()
        });
    }
}
