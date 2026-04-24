using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.Functions
{
    public class TelegramWebhook
    {
        private const string DraftStartMarker = "[EVENT_DRAFT]";
        private const string DraftEndMarker = "[/EVENT_DRAFT]";
        private const string SaveCallbackData = "save_event";

        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly GeminiService _geminiService;

        public TelegramWebhook(ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, GeminiService geminiService)
        {
            _logger = loggerFactory.CreateLogger<TelegramWebhook>();
            _httpClient = httpClientFactory.CreateClient();
            _geminiService = geminiService;
        }

        [Function("TelegramWebhook")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "telegram/webhook")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation($"Received webhook: {requestBody}");

                var update = JsonDocument.Parse(requestBody).RootElement;
                string telegramBotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? string.Empty;

                long chatId = 0;
                long userId = 0;
                string incomingText = string.Empty;
                string? incomingImageUrl = null;
                byte[]? downloadedImageBytes = null;
                string targetIntent = "ASK_TYPE";
                int? messageIdToEdit = null;

                // 1. EXTRACT PAYLOAD (Direct Message OR Callback Query)
                if (update.TryGetProperty("message", out var messageElement))
                {
                    chatId = messageElement.GetProperty("chat").GetProperty("id").GetInt64();
                    userId = messageElement.TryGetProperty("from", out var fromElement) ? fromElement.GetProperty("id").GetInt64() : chatId;
                    
                    if (messageElement.TryGetProperty("text", out var textElement))
                        incomingText = textElement.GetString() ?? string.Empty;
                    else if (messageElement.TryGetProperty("caption", out var captionElement))
                        incomingText = captionElement.GetString() ?? string.Empty;

                    if (messageElement.TryGetProperty("photo", out var photoArray) && photoArray.GetArrayLength() > 0)
                    {
                        var highestResPhoto = photoArray[photoArray.GetArrayLength() - 1];
                        string fileId = highestResPhoto.GetProperty("file_id").GetString() ?? string.Empty;

                        var fileResponse = await _httpClient.GetAsync($"https://api.telegram.org/bot{telegramBotToken}/getFile?file_id={fileId}");
                        if (fileResponse.IsSuccessStatusCode)
                        {
                            var fileJson = JsonDocument.Parse(await fileResponse.Content.ReadAsStringAsync()).RootElement;
                            string filePath = fileJson.GetProperty("result").GetProperty("file_path").GetString() ?? string.Empty;
                            
                            downloadedImageBytes = await _httpClient.GetByteArrayAsync($"https://api.telegram.org/file/bot{telegramBotToken}/{filePath}");
                        }
                    }
                }
                else if (update.TryGetProperty("callback_query", out var callbackQueryElement))
                {
                    var messageObject = callbackQueryElement.GetProperty("message");
                    messageIdToEdit = messageObject.GetProperty("message_id").GetInt32();
                    chatId = messageObject.GetProperty("chat").GetProperty("id").GetInt64();
                    userId = callbackQueryElement.GetProperty("from").GetProperty("id").GetInt64();
                    
                    incomingText = messageObject.GetProperty("text").GetString() ?? string.Empty;
                    string callbackData = callbackQueryElement.GetProperty("data").GetString() ?? string.Empty;
                    string callbackId = callbackQueryElement.GetProperty("id").GetString() ?? string.Empty;

                    // Acknowledge the button press
                    await AnswerCallbackQueryAsync(telegramBotToken, callbackId);

                    if (callbackData == "type_event") targetIntent = "VALIDATE_EVENT";
                    if (callbackData == "type_business") targetIntent = "VALIDATE_BUSINESS";
                    if (callbackData == "type_home") targetIntent = "HOME";
                }
                else
                {
                    return new OkResult(); // Unhandled update
                }

                // 2. DETERMINE INTENT OVERRIDES (Tags in Text)
                if (incomingText.Contains("<<Event>>", StringComparison.OrdinalIgnoreCase)) targetIntent = "VALIDATE_EVENT";
                if (incomingText.Contains("<<Business>>", StringComparison.OrdinalIgnoreCase)) targetIntent = "VALIDATE_BUSINESS";
                if (incomingText.Contains("<<Home>>", StringComparison.OrdinalIgnoreCase)) targetIntent = "HOME";

                // Extract any existing ImageLink carried in the text template
                var imageLinkMatch = Regex.Match(incomingText, @"ImageLink:\s*(https?://[^\s]+)");
                if (imageLinkMatch.Success) incomingImageUrl = imageLinkMatch.Groups[1].Value;

                // 3. EXECUTE INTENT
                object? telegramResponseObj = null;

                if (targetIntent == "HOME")
                {
                    if (downloadedImageBytes != null && string.IsNullOrEmpty(incomingImageUrl))
                    {
                        incomingImageUrl = await _geminiService.UploadImageAsync(downloadedImageBytes, $"image-{DateTime.Now:HHmmss}.jpg", "event-images");
                    }

                    if (!string.IsNullOrEmpty(incomingImageUrl))
                    {
                        // Save Landing image (Temporary logic, can enhance later)
                        telegramResponseObj = new { chat_id = chatId, text = "✅ Home Image Saved." };
                    }
                    else
                    {
                        telegramResponseObj = new { chat_id = chatId, text = "We ideally need an image uploaded to set the Home landing page!" };
                    }
                }
                else
                {
                    string? base64Image = downloadedImageBytes != null ? Convert.ToBase64String(downloadedImageBytes) : null;
                    var geminiResponse = await _geminiService.ProcessIntentAsync(incomingText, base64Image, targetIntent);

                    if (downloadedImageBytes != null && string.IsNullOrEmpty(incomingImageUrl))
                    {
                        string safeName = string.IsNullOrWhiteSpace(geminiResponse.SuggestedFileName) ? "image" : geminiResponse.SuggestedFileName.Replace(" ", "-").Replace("_", "-");
                        string fileName = $"{safeName}-{DateTime.Now:HHmmss}.jpg";
                        incomingImageUrl = await _geminiService.UploadImageAsync(downloadedImageBytes, fileName, "event-images");
                    }

                    if (geminiResponse.Intent == "ASK_TYPE")
                    {
                        string safeImageLink = string.IsNullOrEmpty(incomingImageUrl) ? "" : $"\nImageLink: {incomingImageUrl}";
                        telegramResponseObj = new
                        {
                            chat_id = chatId,
                            text = $"<b>Summary:</b>\n{geminiResponse.Summary}\n\n<b>Original Data for reference:</b> {incomingText}{safeImageLink}\n\nWhat kind of post is this?",
                            parse_mode = "HTML",
                            reply_markup = new
                            {
                                inline_keyboard = new[]
                                {
                                    new[] { new { text = "🎈 Event", callback_data = "type_event" } },
                                    new[] { new { text = "🏢 Business", callback_data = "type_business" } },
                                    new[] { new { text = "🏠 Home Image", callback_data = "type_home" } }
                                }
                            }
                        };
                    }
                    else if (geminiResponse.Intent == "INSTRUCTIONS")
                    {
                        telegramResponseObj = new { chat_id = chatId, text = geminiResponse.UserMessage };
                    }
                    else if (geminiResponse.Intent == "VALIDATE_EVENT")
                    {
                        if (geminiResponse.IsComplete && geminiResponse.EventDetails != null)
                        {
                            geminiResponse.EventDetails.ImageUrl = incomingImageUrl ?? "";
                            string editCode = await _geminiService.SaveEventAsync(geminiResponse.EventDetails, userId);
                            telegramResponseObj = new { chat_id = chatId, text = $"✅ <b>Event Saved!</b>\nEdit Code: {editCode}\n\nTitle: {geminiResponse.EventDetails.Title}\nDate: {geminiResponse.EventDetails.Date}", parse_mode = "HTML" };
                        }
                        else
                        {
                            string safeImageLink = string.IsNullOrEmpty(incomingImageUrl) ? "" : $"\nImageLink: {incomingImageUrl}";
                            telegramResponseObj = new { chat_id = chatId, text = $"❌ <b>Missing Event Info:</b>\n{geminiResponse.UserMessage}\n\n<i>Please COPY the text below, fill in the blanks, and resubmit:</i>\n\n&lt;&lt;Event&gt;&gt;{safeImageLink}\n{incomingText}", parse_mode = "HTML" };
                        }
                    }
                    else if (geminiResponse.Intent == "VALIDATE_BUSINESS")
                    {
                        if (geminiResponse.IsComplete && geminiResponse.BusinessDetails != null)
                        {
                            geminiResponse.BusinessDetails.ImageUrl = incomingImageUrl ?? "";
                            string editCode = await _geminiService.SaveBusinessAsync(geminiResponse.BusinessDetails, userId);
                            telegramResponseObj = new { chat_id = chatId, text = $"✅ <b>Business Saved!</b>\nEdit Code: {editCode}\n\nName: {geminiResponse.BusinessDetails.Name}", parse_mode = "HTML" };
                        }
                        else
                        {
                            string safeImageLink = string.IsNullOrEmpty(incomingImageUrl) ? "" : $"\nImageLink: {incomingImageUrl}";
                            telegramResponseObj = new { chat_id = chatId, text = $"❌ <b>Missing Business Info:</b>\n{geminiResponse.UserMessage}\n\n<i>Please COPY the text below, fill in the blanks, and resubmit:</i>\n\n&lt;&lt;Business&gt;&gt;{safeImageLink}\n{incomingText}", parse_mode = "HTML" };
                        }
                    }
                    else
                    {
                        telegramResponseObj = new { chat_id = chatId, text = "I couldn't process your request safely. Please try again." };
                    }
                }

                // 4. SEND RESPONSE
                if (telegramResponseObj != null)
                {
                    if (messageIdToEdit.HasValue)
                    {
                        await EditTelegramMessageObjAsync(telegramBotToken, chatId, messageIdToEdit.Value, telegramResponseObj);
                    }
                    else
                    {
                        await _httpClient.PostAsJsonAsync($"https://api.telegram.org/bot{telegramBotToken}/sendMessage", telegramResponseObj);
                    }
                }

                return new OkResult();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Detailed error: {ex.Message} \n {ex.StackTrace}");
                return new OkResult();
            }
        }

        private static bool IsDraftMessage(string text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && text.Contains(DraftStartMarker, StringComparison.OrdinalIgnoreCase)
                && text.Contains(DraftEndMarker, StringComparison.OrdinalIgnoreCase);
        }

        private async Task HandleCallbackQueryAsync(JsonElement callbackQueryElement, string telegramBotToken)
        {
            var callbackId = callbackQueryElement.GetProperty("id").GetString() ?? string.Empty;
            var callbackData = callbackQueryElement.GetProperty("data").GetString() ?? string.Empty;

            await AnswerCallbackQueryAsync(telegramBotToken, callbackId);

            if (!callbackData.Equals(SaveCallbackData, StringComparison.Ordinal))
            {
                return;
            }

            if (!callbackQueryElement.TryGetProperty("message", out var callbackMessage))
            {
                return;
            }

            long chatId = callbackMessage.GetProperty("chat").GetProperty("id").GetInt64();
            long userId = callbackQueryElement.GetProperty("from").GetProperty("id").GetInt64();
            int messageId = callbackMessage.GetProperty("message_id").GetInt32();
            string previewText = callbackMessage.GetProperty("text").GetString() ?? string.Empty;

            if (!IsDraftMessage(previewText))
            {
                await EditTelegramMessageAsync(
                    telegramBotToken,
                    chatId,
                    messageId,
                    "I could not find a valid event draft in that message. Please submit the event again.");
                return;
            }

            var eventData = ParseDraftMessage(previewText);
            string editCode = await _geminiService.SaveEventAsync(eventData, userId);

            await EditTelegramMessageAsync(
                telegramBotToken,
                chatId,
                messageId,
                $"[Saved] Event saved. Edit code: {editCode}\n\nTitle: {DisplayValue(eventData.Title)}\nDate: {DisplayValue(eventData.Date)}\nTime: {DisplayValue(eventData.Time)}");
        }

        private async Task AnswerCallbackQueryAsync(string telegramBotToken, string callbackId)
        {
            if (string.IsNullOrWhiteSpace(callbackId))
            {
                return;
            }

            await _httpClient.PostAsJsonAsync(
                $"https://api.telegram.org/bot{telegramBotToken}/answerCallbackQuery",
                new
                {
                    callback_query_id = callbackId
                });
        }

        private async Task EditTelegramMessageAsync(string telegramBotToken, long chatId, int messageId, string text)
        {
            await _httpClient.PostAsJsonAsync(
                $"https://api.telegram.org/bot{telegramBotToken}/editMessageText",
                new
                {
                    chat_id = chatId,
                    message_id = messageId,
                    text
                });
        }

        private async Task EditTelegramMessageObjAsync(string telegramBotToken, long chatId, int messageId, object requestPayload)
        {
            var editPayload = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(requestPayload));
            if (editPayload != null)
            {
                editPayload["message_id"] = messageId;
                await _httpClient.PostAsJsonAsync($"https://api.telegram.org/bot{telegramBotToken}/editMessageText", editPayload);
            }
        }

        private static object BuildSaveKeyboard()
        {
            return new
            {
                inline_keyboard = new object[]
                {
                    new object[]
                    {
                        new
                        {
                            text = "Save",
                            callback_data = SaveCallbackData
                        }
                    }
                }
            };
        }

        private static EventDto ParseDraftMessage(string text)
        {
            return new EventDto
            {
                Title = ExtractField(text, "Title"),
                Date = ExtractField(text, "Date"),
                Time = ExtractField(text, "Time"),
                Location = ExtractField(text, "Location"),
                Description = ExtractField(text, "Description"),
                Category = ExtractField(text, "Category"),
                ImageUrl = ExtractField(text, "Image")
            };
        }

        private static string BuildDraftMessage(EventDto eventData)
        {
            return string.Join("\n", new[]
            {
                DraftStartMarker,
                $"Title: {DisplayValue(eventData.Title)}",
                $"Date: {DisplayValue(eventData.Date)}",
                $"Time: {DisplayValue(eventData.Time)}",
                $"Location: {DisplayValue(eventData.Location)}",
                $"Description: {DisplayValue(eventData.Description)}",
                $"Category: {DisplayValue(eventData.Category)}",
                $"Image: {DisplayValue(eventData.ImageUrl)}",
                DraftEndMarker
            });
        }

        private static string ExtractField(string text, string fieldName)
        {
            var pattern = $@"(?im)^{Regex.Escape(fieldName)}:\s*(.*)$";
            var match = Regex.Match(text, pattern);
            if (!match.Success)
            {
                return string.Empty;
            }

            var value = match.Groups[1].Value.Trim();
            return value.Equals("(blank)", StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(blank)" : value.Trim();
        }
    }
}
