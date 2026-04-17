using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace api.Services
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GeminiService> _logger;

        public GeminiService(IHttpClientFactory httpClientFactory, ILogger<GeminiService> logger)
        {
            _httpClient = httpClientFactory.CreateClient("Gemini");
            _logger = logger;
        }

        public async Task<EventDto> ExtractEventAsync(string textContext, string? base64Image)
        {
            string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("Gemini API Key missing.");

            var requestUri = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

var systemInstruction = @"You are a data extraction assistant for the Pittsburgh Indian Community portal.
Extract event details from the user's text and/or image.
Return ONLY valid JSON in this exact structure, with no markdown formatting:
{
  ""Title"": """",
  ""Date"": """",
  ""Time"": """",
  ""Location"": """",
  ""Description"": """",
  ""Category"": """",
  ""ErrorMessage"": """",
  ""SuggestedFileName"": """"
}
If something is missing, infer it reasonably or leave it blank.
If you find an event, provide a short 1-to-3 word SuggestedFileName (e.g. 'DiwaliMela', 'TechMeetup'). 
If the provided text or image does not contain any event information whatsoever, set ErrorMessage to a helpful response, set SuggestedFileName to 'Non-event', and leave the other fields blank.";

            object payload;

            if (!string.IsNullOrEmpty(base64Image))
            {
                payload = new
                {
                    system_instruction = new { parts = new[] { new { text = systemInstruction } } },
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = $"Extract the details for this event poster. Additional context: {textContext}" },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = "image/jpeg",
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    }
                };
            }
            else
            {
                payload = new
                {
                    system_instruction = new { parts = new[] { new { text = systemInstruction } } },
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = $"Extract the event details from this message: {textContext}" }
                            }
                        }
                    }
                };
            }

            var response = await _httpClient.PostAsJsonAsync(requestUri, payload);
            var responseJson = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Gemini API Error: {responseJson}");
                throw new Exception("Failed to process with Gemini.");
            }

            using var document = JsonDocument.Parse(responseJson);
            var textResult = document.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text").GetString();

            // Clean markdown blocks if present
            var cleanJson = Regex.Replace(textResult ?? string.Empty, @"```json\s*|\s*```", "").Trim();
            
            _logger.LogInformation($"Extracted JSON: {cleanJson}");
            var eventData = JsonSerializer.Deserialize<EventDto>(cleanJson);

            return eventData ?? new EventDto();
        }

        public async Task<string> SaveEventAsync(EventDto eventData, long telegramUserId)
        {
            if (eventData == null) throw new ArgumentNullException(nameof(eventData));
            string editCode = Guid.NewGuid().ToString("N").Substring(0, 5).ToUpper();
            eventData.EditCode = editCode;
            eventData.TelegramUserId = telegramUserId;
            
            await SaveToTableStorageAsync(eventData);
            return editCode;
        }

        private async Task SaveToTableStorageAsync(EventDto eventData)
        {
            try
            {
                string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
                var serviceClient = new TableServiceClient(connectionString);
                var tableClient = serviceClient.GetTableClient("Events");
                await tableClient.CreateIfNotExistsAsync();

                var eventEntity = new TableEntity("Event", Guid.NewGuid().ToString())
                {
                    { "Title", eventData.Title },
                    { "Date", eventData.Date },
                    { "Time", eventData.Time },
                    { "Location", eventData.Location },
                    { "Description", eventData.Description },
                    { "Category", eventData.Category },
                    { "ImageUrl", eventData.ImageUrl },
                    { "EditCode", eventData.EditCode },
                    { "IsApproved", true }, // Automatically posted
                    { "SubmittedByUserId", eventData.TelegramUserId }
                };

                await tableClient.AddEntityAsync(eventEntity);
                _logger.LogInformation($"Successfully saved event to Azure Table: {eventData.Title}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save to Table Storage.");
                throw;
            }
        }

        public async Task<string> UploadImageAsync(byte[] imageBytes, string fileName, string containerName)
        {
            if (imageBytes == null || imageBytes.Length == 0) return string.Empty;

            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);

            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = new System.IO.MemoryStream(imageBytes);
            await blobClient.UploadAsync(stream, overwrite: true);

            return blobClient.Uri.ToString();
        }
    }

    public class EventDto
    {
        public string Title { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string SuggestedFileName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string EditCode { get; set; } = string.Empty;
        public long? TelegramUserId { get; set; }
    }
}
