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
            _logger.LogInformation("C# HTTP trigger function received a Telegram webhook request.");

            string telegramBotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "";
            long? currentChatId = null;

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                using var document = JsonDocument.Parse(requestBody);
                var root = document.RootElement;

                if (root.TryGetProperty("callback_query", out var callbackQueryElement))
                {
                    if (callbackQueryElement.TryGetProperty("message", out var cbMessage) && cbMessage.TryGetProperty("chat", out var cbChat) && cbChat.TryGetProperty("id", out var cbId))
                    {
                        currentChatId = cbId.GetInt64();
                    }
                    await HandleCallbackQueryAsync(callbackQueryElement, telegramBotToken);
                    return new OkResult();
                }

                if (!root.TryGetProperty("message", out var messageElement))
                {
                    return new OkResult();
                }

                currentChatId = messageElement.GetProperty("chat").GetProperty("id").GetInt64();
                long chatId = currentChatId.Value;
                long userId = messageElement.TryGetProperty("from", out var fromElement) ? fromElement.GetProperty("id").GetInt64() : chatId;
                
                string text = string.Empty;
                string? imageBytesBase64 = null;
                string? imageUrl = null;
                byte[]? downloadedImageBytes = null;

                if (messageElement.TryGetProperty("text", out var textElement))
                {
                    text = textElement.GetString() ?? string.Empty;
                }
                else if (messageElement.TryGetProperty("caption", out var captionElement))
                {
                    text = captionElement.GetString() ?? string.Empty;
                }

                // Handle Image (photo array)
                if (messageElement.TryGetProperty("photo", out var photoArray) && photoArray.GetArrayLength() > 0)
                {
                    // Get the highest resolution photo (last element)
                    var bestPhoto = photoArray[photoArray.GetArrayLength() - 1];
                    string fileId = bestPhoto.GetProperty("file_id").GetString() ?? string.Empty;

                    // Get File Path from Telegram
                    var fileResponse = await _httpClient.GetAsync($"https://api.telegram.org/bot{telegramBotToken}/getFile?file_id={fileId}");
                    var fileJson = await fileResponse.Content.ReadFromJsonAsync<JsonElement>();
                    if (fileJson.GetProperty("ok").GetBoolean())
                    {
                        string filePath = fileJson.GetProperty("result").GetProperty("file_path").GetString() ?? string.Empty;
                        
                        // Download Image
                        downloadedImageBytes = await _httpClient.GetByteArrayAsync($"https://api.telegram.org/file/bot{telegramBotToken}/{filePath}");
                        imageBytesBase64 = Convert.ToBase64String(downloadedImageBytes);
                    }
                }

                if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(imageBytesBase64))
                {
                     return new OkResult();
                }

                object previewResponseMsg;

                if (IsDraftMessage(text))
                {
                    var eventData = ParseDraftMessage(text);
                    string editCode = await _geminiService.SaveEventAsync(eventData, userId);

                    previewResponseMsg = new
                    {
                        chat_id = chatId,
                        text = $"✅ Event saved.\nEdit Code: {editCode}\n\nTitle: {DisplayValue(eventData.Title)}\nDate: {DisplayValue(eventData.Date)}\nTime: {DisplayValue(eventData.Time)}"
                    };
                }
                else
                {
                    var extractedEvent = await _geminiService.ExtractEventAsync(text, imageBytesBase64);

                    if (downloadedImageBytes != null)
                    {
                        string safeName = string.IsNullOrWhiteSpace(extractedEvent.SuggestedFileName) ? "Event" : extractedEvent.SuggestedFileName;
                        safeName = Regex.Replace(safeName, "[^a-zA-Z0-9-]", "");
                        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Image";
                        
                        string timestamp = DateTime.Now.ToString("HHmmss");
                        string finalFileName = $"{safeName}_{timestamp}.jpg";
                        
                        imageUrl = await _geminiService.UploadImageAsync(downloadedImageBytes, finalFileName, "event-images");
                    }

                    if (!string.IsNullOrWhiteSpace(extractedEvent.ErrorMessage))
                    {
                        previewResponseMsg = new
                        {
                            chat_id = chatId,
                            text = extractedEvent.ErrorMessage
                        };
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            extractedEvent.ImageUrl = imageUrl;
                        }
                        var previewMessage = BuildDraftMessage(extractedEvent);

                        previewResponseMsg = new
                        {
                            chat_id = chatId,
                            text = "I extracted these event details. Edit and send this block back if you want changes, or tap Save if it looks good.\n\n" + previewMessage,
                            reply_markup = BuildSaveKeyboard()
                        };
                    }
                }

                await _httpClient.PostAsJsonAsync($"https://api.telegram.org/bot{telegramBotToken}/sendMessage", previewResponseMsg);

                return new OkResult();

                #if false
                // Notify User Success
                var responseMsg = new { chat_id = chatId, text = "✅ Event received! Processed by Gemini 1.5 and added to the community directory instantly." };
                await _httpClient.PostAsJsonAsync($"https://api.telegram.org/bot{telegramBotToken}/sendMessage", responseMsg);

                return new OkResult();
                #endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                
                if (currentChatId.HasValue && !string.IsNullOrEmpty(telegramBotToken))
                {
                    try
                    {
                        var errorMsg = new
                        {
                            chat_id = currentChatId.Value,
                            text = $"❌ An error occurred: {ex.Message}"
                        };
                        await _httpClient.PostAsJsonAsync($"https://api.telegram.org/bot{telegramBotToken}/sendMessage", errorMsg);
                    }
                    catch { /* Ignore */ }
                }

                // Always return 200 OK to Telegram so it stops retrying the failed message
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
