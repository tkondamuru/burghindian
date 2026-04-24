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

        public async Task<AgentResponseDto> ProcessIntentAsync(string text, string? base64Image, string targetIntent)
        {
            var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Gemini API key is not configured.");
            }

            var requestUri = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            string allowedTagsList = "'Indian Food', 'Restaurants', 'Temples', 'Grocery', 'Attraction', 'Children', 'Adventure', 'Professional Services', 'Community', 'Arts & Culture'";

            string intentInstruction = "";
            if (targetIntent == "VALIDATE_EVENT")
            {
                intentInstruction = $@"You must extract an EVENT. Required fields: Title, Date, Location.
For 'Category' and 'Tags', you MUST ONLY pick exact strings from this list (or leave blank if none fit): {allowedTagsList}.
Ensure 'Description' contains ONLY the original marketing text or rules, while 'Summary' contains a concise 1-sentence overview you write.
If complete, set IsComplete to true. Set Intent to 'VALIDATE_EVENT'.
If incomplete, set IsComplete to false and write nicely in UserMessage exactly what details are missing (e.g., 'Please provide the Date and Location').";
            }
            else if (targetIntent == "VALIDATE_BUSINESS")
            {
                intentInstruction = $@"You must extract a BUSINESS. Required fields: Name, Address.
For 'Category' and 'Tags', you MUST ONLY pick exact strings from this list (or leave blank if none fit): {allowedTagsList}.
Ensure 'Description' contains ONLY the original marketing text, while 'Summary' contains a concise 1-sentence overview you write.
If complete, set IsComplete to true. Set Intent to 'VALIDATE_BUSINESS'.
If incomplete, set IsComplete to false and write nicely in UserMessage exactly what details are missing.";
            }
            else
            {
                intentInstruction = @"If it's conversational like 'Hi', set Intent to 'INSTRUCTIONS' and UserMessage to a helpful greeting. 
If it contains data, set Intent to 'ASK_TYPE', leave details empty, summarize the content in Summary, and set UserMessage to 'Is this an Event, Business, or Home page?'";
            }

            var systemInstruction = $@"You are a data extraction assistant for the Pittsburgh Indian Community portal.
Return ONLY valid JSON in this exact structure, with no markdown formatting:
{{
  ""Intent"": ""ASK_TYPE | VALIDATE_EVENT | VALIDATE_BUSINESS | INSTRUCTIONS"",
  ""IsComplete"": false,
  ""Summary"": """",
  ""UserMessage"": """",
  ""SuggestedFileName"": """",
  ""ErrorMessage"": """",
  ""EventDetails"": {{ ""Title"": """", ""Date"": """", ""Time"": """", ""Location"": """", ""Summary"": """", ""Description"": """", ""Category"": """", ""Tags"": """" }},
  ""BusinessDetails"": {{ ""Name"": """", ""Address"": """", ""Phone"": """", ""Summary"": """", ""Description"": """", ""Category"": """", ""Tags"": """" }}
}}

{intentInstruction}

If you find usable media/data, provide a short 1-3 word SuggestedFileName (e.g. 'DiwaliMela', 'TechMeetup'). Otherwise leave it empty.";

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
                                new { text = $"Extract the details for this content. Additional context: {text}" },
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
                                new { text = $"Extract the details from this message: {text}" }
                            }
                        }
                    }
                };
            }

            try
            {
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

                var cleanJson = Regex.Replace(textResult ?? string.Empty, @"```json\s*|\s*```", "").Trim();
                
                _logger.LogInformation($"Extracted JSON: {cleanJson}");
                var agentResponse = JsonSerializer.Deserialize<AgentResponseDto>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return agentResponse ?? new AgentResponseDto { ErrorMessage = "Failed to deserialize agent response." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini API for intent.");
                return new AgentResponseDto { ErrorMessage = $"An error occurred during extraction: {ex.Message}" };
            }
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

                var tableEntity = new TableEntity(eventData.Category ?? "General", Guid.NewGuid().ToString())
                {
                    { "Title", eventData.Title },
                    { "Date", eventData.Date },
                    { "Time", eventData.Time },
                    { "Location", eventData.Location },
                    { "Summary", eventData.Summary },
                    { "Description", eventData.Description },
                    { "Category", eventData.Category },
                    { "Tags", eventData.Tags },
                    { "ImageUrl", eventData.ImageUrl },
                    { "EditCode", eventData.EditCode },
                    { "IsApproved", true }, // Automatically posted
                    { "SubmittedByUserId", eventData.TelegramUserId }
                };

                await tableClient.AddEntityAsync(tableEntity);
                _logger.LogInformation($"Successfully saved event to Azure Table: {eventData.Title}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save to Table Storage.");
                throw;
            }
        }

        public async Task<string> SaveBusinessAsync(BusinessDto businessData, long telegramUserId)
        {
            if (businessData == null) throw new ArgumentNullException(nameof(businessData));
            string editCode = Guid.NewGuid().ToString("N").Substring(0, 5).ToUpper();
            businessData.EditCode = editCode;
            businessData.TelegramUserId = telegramUserId;
            
            try
            {
                string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
                var serviceClient = new TableServiceClient(connectionString);
                var tableClient = serviceClient.GetTableClient("Businesses");
                await tableClient.CreateIfNotExistsAsync();

                var tableEntity = new TableEntity(businessData.Category ?? "General", Guid.NewGuid().ToString())
                {
                    { "Name", businessData.Name },
                    { "Address", businessData.Address },
                    { "Phone", businessData.Phone },
                    { "Summary", businessData.Summary },
                    { "Description", businessData.Description },
                    { "Category", businessData.Category },
                    { "Tags", businessData.Tags },
                    { "ImageUrl", businessData.ImageUrl },
                    { "EditCode", businessData.EditCode },
                    { "IsApproved", true },
                    { "SubmittedByUserId", businessData.TelegramUserId }
                };

                await tableClient.AddEntityAsync(tableEntity);
                return editCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving business to Table Storage.");
                throw;
            }
        }

        public async Task<string> UploadImageAsync(byte[] imageBytes, string fileName, string containerName)
        {
            if (imageBytes == null || imageBytes.Length == 0) return string.Empty;

            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync();

            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = new System.IO.MemoryStream(imageBytes);
            await blobClient.UploadAsync(stream, overwrite: true);

            return blobClient.Uri.ToString();
        }
    }

    public class AgentResponseDto
    {
        public string Intent { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string UserMessage { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string SuggestedFileName { get; set; } = string.Empty;
        public EventDto? EventDetails { get; set; }
        public BusinessDto? BusinessDetails { get; set; }
    }

    public class EventDto
    {
        public string Title { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string EditCode { get; set; } = string.Empty;
        public long? TelegramUserId { get; set; }
    }

    public class BusinessDto
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string EditCode { get; set; } = string.Empty;
        public long? TelegramUserId { get; set; }
    }
}
