using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;

namespace api.Functions
{
    public class ListEventsFunction
    {
        private readonly ILogger _logger;

        public ListEventsFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ListEventsFunction>();
        }

        [Function("ListEvents")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "events")] HttpRequest req)
        {
            _logger.LogInformation("ListEvents endpoint triggered.");
            
            // Allow preflight OPTIONS
            req.HttpContext.Response.Headers.Append("Access-Control-Allow-Origin", "*");
            req.HttpContext.Response.Headers.Append("Access-Control-Allow-Methods", "GET, OPTIONS");
            req.HttpContext.Response.Headers.Append("Access-Control-Allow-Headers", "*");
            if (HttpMethods.IsOptions(req.Method))
            {
                return new OkResult();
            }

            try
            {
                string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
                var serviceClient = new TableServiceClient(connectionString);
                var tableClient = serviceClient.GetTableClient("Events");
                await tableClient.CreateIfNotExistsAsync();

                var events = new List<object>();

                await foreach (var entity in tableClient.QueryAsync<TableEntity>("IsApproved eq true"))
                {
                    events.Add(new
                    {
                        Id = entity.RowKey,
                        Title = entity.GetString("Title"),
                        Date = entity.GetString("Date"),
                        Time = entity.GetString("Time"),
                        Location = entity.GetString("Location"),
                        Description = entity.GetString("Description"),
                        Category = entity.GetString("Category"),
                        ImageUrl = entity.GetString("ImageUrl")
                    });
                }

                return new OkObjectResult(events);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching events.");
                return new StatusCodeResult(500);
            }
        }
    }
}
