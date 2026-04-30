using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using api.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("Gemini");

// Register GooglePlacesService
builder.Services.AddSingleton<GooglePlacesService>();

// Register GeminiService explicitly so it can be used for storage logic
builder.Services.AddSingleton<GeminiService>();

// Toggle AI Service based on environment variable
var useAzureAi = Environment.GetEnvironmentVariable("USE_AZURE_AI")?.ToLower() == "true";
if (useAzureAi)
{
    builder.Services.AddSingleton<IAiService, AzureAiService>();
}
else
{
    builder.Services.AddSingleton<IAiService, GeminiService>();
}

builder.Services.AddSingleton<TelegramMappingService>();

builder.Build().Run();
