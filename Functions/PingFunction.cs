using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.Functions
{
    public class PingFunction
    {
        private readonly ILogger _logger;

        public PingFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<PingFunction>();
        }

        [Function("Ping")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            _logger.LogInformation("Ping endpoint was triggered.");

            return new OkObjectResult("Pong! The Azure Function is running successfully.");
        }
    }
}
