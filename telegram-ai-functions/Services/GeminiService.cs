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
    public class GeminiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GeminiService> _logger;
        private readonly GooglePlacesService _placesService;

        public GeminiService(IHttpClientFactory httpClientFactory, ILogger<GeminiService> logger, GooglePlacesService placesService)
        {
            _httpClient = httpClientFactory.CreateClient("Gemini");
            _logger = logger;
            _placesService = placesService;
        }

        public async Task<AgentResponseDto> ProcessIntentAsync(string text, string? base64Image, string targetIntent)
        {
            var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Gemini API key is not configured.");
            }

            var requestUri = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            string eventTagsList = "'community', 'culture', 'family', 'food', 'temple', 'professional', 'kids', 'other'";
            string businessTagsList = "'restaurant', 'grocery', 'temple', 'service', 'shopping', 'education', 'health', 'other'";

            string intentInstruction = "";
            if (targetIntent == "VALIDATE_EVENT")
            {
                intentInstruction = $@"You must extract an EVENT. Required fields: Title, Date, Location, Description.
1. You MUST generate a 1-sentence 'Summary' yourself from the description. Do NOT leave it empty or [MISSING].
2. For 'Category' and 'Tags', you MUST ONLY pick comma-separated exact strings from this list (if none fit, select 'other'): {eventTagsList}.
If complete, set IsComplete to true. Set Intent to 'VALIDATE_EVENT'.
If incomplete:
- Set IsComplete to false.
- Fill 'EventDetails' with EVERYTHING you found.
- In 'UserMessage', provide this template:
Confirm Event Info:
Title: [Value or [MISSING]]
Date: [Value or [MISSING]]
Time: [Value or [MISSING]]
Location: [Value or [MISSING]]
Description: [Value or [MISSING]]
Category: [Value]
Tags: [Value]

Please COPY the text above, fill in any [MISSING] blanks, and resubmit.
<<Event>>";
            }
            else if (targetIntent == "VALIDATE_BUSINESS")
            {
                intentInstruction = $@"You must extract a BUSINESS. Required fields: Name, Address, Description.
1. You MUST generate a 1-sentence 'Summary' yourself from the description. Do NOT leave it empty or [MISSING].
2. For 'Category' and 'Tags', you MUST ONLY pick comma-separated exact strings from this list (if none fit, select 'other'): {businessTagsList}.
If complete, set IsComplete to true. Set Intent to 'VALIDATE_BUSINESS'.
If incomplete:
- Set IsComplete to false.
- Fill 'BusinessDetails' with EVERYTHING you found.
- In 'UserMessage', provide this template:
Confirm Business Info:
Name: [Value or [MISSING]]
Address: [Value or [MISSING]]
Phone: [Value or [MISSING]]
Description: [Value or [MISSING]]
Category: [Value]
Tags: [Value]

Please COPY the text above, fill in any [MISSING] blanks, and resubmit.
<<Business>>";
            }
            else
            {
                intentInstruction = $@"If it's conversational like 'Hi', set Intent to 'INSTRUCTIONS' and UserMessage to a helpful greeting. 
If it contains data:
1. Set Intent to 'ASK_TYPE'.
2. EXTRACT EVERYTHING you can into BOTH 'EventDetails' and 'BusinessDetails' simultaneously.
3. For 'Tags', use these: Event({eventTagsList}), Business({businessTagsList}).
4. In 'Summary', write a clear reconstruction of all data found (e.g., 'Name: X, Date: Y...').
5. Set 'UserMessage' to 'I found some details! Is this an Event or a Business?'";
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
                    string errorMessage = "Failed to process with Gemini.";
                    try
                    {
                        using var errorDoc = JsonDocument.Parse(responseJson);
                        if (errorDoc.RootElement.TryGetProperty("error", out var errorElement) &&
                            errorElement.TryGetProperty("message", out var messageProp))
                        {
                            errorMessage = messageProp.GetString() ?? errorMessage;
                        }
                    }
                    catch { /* Fallback to default */ }

                    return new AgentResponseDto { ErrorMessage = errorMessage };
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

        public async Task<string> SaveEventAsync(EventDto eventData, long telegramUserId, string submitterEmail)
        {
            if (eventData == null) throw new ArgumentNullException(nameof(eventData));
            string editCode = Guid.NewGuid().ToString("N").Substring(0, 5).ToUpper();
            eventData.EditCode = editCode;
            eventData.TelegramUserId = telegramUserId;
            
            await SaveToTableStorageAsync(eventData, submitterEmail);
            return editCode;
        }

        private async Task SaveToTableStorageAsync(EventDto eventData, string submitterEmail)
        {
            try
            {
                string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
                var serviceClient = new TableServiceClient(connectionString);
                var tableClient = serviceClient.GetTableClient("Events");
                var lookupTable = serviceClient.GetTableClient("EditCodeLookup");
                await tableClient.CreateIfNotExistsAsync();
                await lookupTable.CreateIfNotExistsAsync();

                var createdAt = DateTimeOffset.UtcNow;
                var partitionKey = createdAt.ToString("yyyy-MM");
                var suffix = Guid.NewGuid().ToString("N").Substring(0, 5).ToUpperInvariant();
                var rowKey = $"{createdAt:yyyyMMddHHmmssfff}-{suffix}";
                var editCode = eventData.EditCode;

                var tableEntity = new TableEntity(partitionKey, rowKey)
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
                    { "EditCode", editCode },
                    { "IsApproved", true },
                    { "SubmittedByUserId", eventData.TelegramUserId },
                    { "SubmitterEmail", submitterEmail },
                    { "Source", "telegram-bot" },
                    { "CreatedAtUtc", createdAt.ToString("O") },
                    { "UpdatedAtUtc", createdAt.ToString("O") }
                };

                await tableClient.AddEntityAsync(tableEntity);

                var lookupEntity = new TableEntity("edit", editCode)
                {
                    { "EntityType", "Event" },
                    { "TargetTable", "Events" },
                    { "TargetPartitionKey", partitionKey },
                    { "TargetRowKey", rowKey },
                    { "SubmitterEmail", submitterEmail },
                    { "IsApproved", true },
                    { "CreatedAtUtc", createdAt.ToString("O") }
                };
                await lookupTable.AddEntityAsync(lookupEntity);

                _logger.LogInformation($"Successfully saved event to Azure Table: {eventData.Title}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save to Table Storage.");
                throw;
            }
        }

        public async Task<string> SaveBusinessAsync(BusinessDto businessData, long telegramUserId, string submitterEmail)
        {
            if (businessData == null) throw new ArgumentNullException(nameof(businessData));
            string editCode = Guid.NewGuid().ToString("N").Substring(0, 5).ToUpperInvariant();
            businessData.EditCode = editCode;
            businessData.TelegramUserId = telegramUserId;
            
            try
            {
                string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
                var serviceClient = new TableServiceClient(connectionString);
                var tableClient = serviceClient.GetTableClient("Businesses");
                var lookupTable = serviceClient.GetTableClient("EditCodeLookup");
                await tableClient.CreateIfNotExistsAsync();
                await lookupTable.CreateIfNotExistsAsync();

                var createdAt = DateTimeOffset.UtcNow;
                var partitionKey = createdAt.ToString("yyyy-MM");
                var suffix = Guid.NewGuid().ToString("N").Substring(0, 5).ToUpperInvariant();
                var rowKey = $"{createdAt:yyyyMMddHHmmssfff}-{suffix}";

                var tableEntity = new TableEntity(partitionKey, rowKey)
                {
                    { "Name", businessData.Name },
                    { "Address", businessData.Address },
                    { "Phone", businessData.Phone },
                    { "Summary", businessData.Summary },
                    { "Description", businessData.Description },
                    { "Category", businessData.Category },
                    { "Tags", businessData.Tags },
                    { "ImageUrl", businessData.ImageUrl },
                    { "EditCode", editCode },
                    { "IsApproved", true },
                    { "SubmittedByUserId", businessData.TelegramUserId },
                    { "SubmitterEmail", submitterEmail },
                    { "Source", "telegram-bot" },
                    { "CreatedAtUtc", createdAt.ToString("O") },
                    { "UpdatedAtUtc", createdAt.ToString("O") }
                };

                await tableClient.AddEntityAsync(tableEntity);

                var lookupEntity = new TableEntity("edit", editCode)
                {
                    { "EntityType", "Business" },
                    { "TargetTable", "Businesses" },
                    { "TargetPartitionKey", partitionKey },
                    { "TargetRowKey", rowKey },
                    { "SubmitterEmail", submitterEmail },
                    { "IsApproved", true },
                    { "CreatedAtUtc", createdAt.ToString("O") }
                };
                await lookupTable.AddEntityAsync(lookupEntity);

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

        public async Task EnrichBusinessAsync(BusinessDto businessData)
        {
            if (businessData == null) return;
            string businessId = businessData.Name.Replace(" ", "-").ToLower() + "-" + DateTime.Now.ToString("yyyyMMdd");
            var photoUrls = await _placesService.EnrichBusinessPhotosAsync(businessData.Name, businessData.Address, businessId);
            if (photoUrls != null && photoUrls.Count > 0)
            {
                // We'll store them as a semicolon separated list in the ImageUrl field for now
                // Or you can create a new 'Gallery' field in your table later
                businessData.ImageUrl = string.Join(";", photoUrls);
            }
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
