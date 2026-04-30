using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace api.Services
{
    public class AzureAiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AzureAiService> _logger;
        private readonly GeminiService _storageHelper; // We'll reuse the storage logic from GeminiService for now to avoid duplication

        public AzureAiService(IHttpClientFactory httpClientFactory, ILogger<AzureAiService> logger, GeminiService storageHelper)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _storageHelper = storageHelper;
        }

        public async Task<AgentResponseDto> ProcessIntentAsync(string text, string? base64Image, string targetIntent)
        {
            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            {
                return new AgentResponseDto { ErrorMessage = "Azure OpenAI is not configured." };
            }

            // Clean up endpoint to ensure no trailing slash
            var baseEndpoint = endpoint.TrimEnd('/');
            var requestUri = $"{baseEndpoint}/openai/deployments/{deploymentName}/chat/completions?api-version=2025-01-01-preview";

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
                intentInstruction = $@"If it's conversational like 'Hi' or 'Hello', set Intent to 'INSTRUCTIONS' and UserMessage to a helpful greeting. 
If it contains data:
1. Set Intent to 'ASK_TYPE'.
2. EXTRACT EVERYTHING you can into BOTH 'EventDetails' and 'BusinessDetails' simultaneously.
3. For 'Tags', use these: Event({eventTagsList}), Business({businessTagsList}).
4. In 'Summary', write a clear reconstruction of all data found (e.g., 'Name: X, Date: Y...').
5. Set 'UserMessage' to 'I found some details! Is this an Event or a Business?'";
            }

            var systemPrompt = $@"You are a data extraction assistant.
Return ONLY valid JSON:
{{
  ""Intent"": ""ASK_TYPE | VALIDATE_EVENT | VALIDATE_BUSINESS | INSTRUCTIONS"",
  ""IsComplete"": false,
  ""Summary"": """",
  ""UserMessage"": """",
  ""SuggestedFileName"": """",
  ""EventDetails"": {{ ""Title"": """", ""Date"": """", ""Time"": """", ""Location"": """", ""Summary"": """", ""Description"": """", ""Category"": """", ""Tags"": """" }},
  ""BusinessDetails"": {{ ""Name"": """", ""Address"": """", ""Phone"": """", ""Summary"": """", ""Description"": """", ""Category"": """", ""Tags"": """" }}
}}

{intentInstruction}";

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            if (!string.IsNullOrEmpty(base64Image))
            {
                messages.Add(new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = text },
                        new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64Image}" } }
                    }
                });
            }
            else
            {
                messages.Add(new { role = "user", content = text });
            }

            var payload = new { messages = messages, response_format = new { type = "json_object" } };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
                request.Headers.Add("api-key", apiKey);
                request.Content = JsonContent.Create(payload);

                var response = await _httpClient.SendAsync(request);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Azure OpenAI Error: {responseJson}");
                    return new AgentResponseDto { ErrorMessage = $"Azure OpenAI Error: {response.StatusCode}" };
                }

                using var doc = JsonDocument.Parse(responseJson);
                var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

                var result = JsonSerializer.Deserialize<AgentResponseDto>(content ?? "{}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result ?? new AgentResponseDto { ErrorMessage = "Failed to parse Azure response." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure AI Service Error");
                return new AgentResponseDto { ErrorMessage = ex.Message };
            }
        }

        public Task<string> SaveEventAsync(EventDto eventData, long telegramUserId, string submitterEmail) => _storageHelper.SaveEventAsync(eventData, telegramUserId, submitterEmail);
        public Task<string> SaveBusinessAsync(BusinessDto businessData, long telegramUserId, string submitterEmail) => _storageHelper.SaveBusinessAsync(businessData, telegramUserId, submitterEmail);
        public Task<string> UploadImageAsync(byte[] imageBytes, string fileName, string containerName) => _storageHelper.UploadImageAsync(imageBytes, fileName, containerName);
        public Task EnrichBusinessAsync(BusinessDto businessData) => _storageHelper.EnrichBusinessAsync(businessData);
    }
}
