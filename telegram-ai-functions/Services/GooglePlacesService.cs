using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace api.Services
{
    public class GooglePlacesService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GooglePlacesService> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public GooglePlacesService(IHttpClientFactory httpClientFactory, ILogger<GooglePlacesService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
            _blobServiceClient = new BlobServiceClient(connectionString);
        }

        public async Task<List<string>> EnrichBusinessPhotosAsync(string name, string address, string businessId)
        {
            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY");
            if (string.IsNullOrEmpty(apiKey)) 
            {
                _logger.LogWarning("GOOGLE_MAPS_API_KEY is not configured. Skipping enrichment.");
                return new List<string>();
            }

            try
            {
                // 1. Search for Place ID
                string query = Uri.EscapeDataString($"{name} {address}");
                var searchUrl = $"https://maps.googleapis.com/maps/api/place/findplacefromtext/json?input={query}&inputtype=textquery&fields=place_id&key={apiKey}";
                
                var searchResponse = await _httpClient.GetStringAsync(searchUrl);
                using var searchDoc = JsonDocument.Parse(searchResponse);
                
                if (!searchDoc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                {
                    _logger.LogInformation($"No place found for: {name} {address}");
                    return new List<string>();
                }

                string placeId = candidates[0].GetProperty("place_id").GetString()!;

                // 2. Get Place Details (Photos)
                var detailsUrl = $"https://maps.googleapis.com/maps/api/place/details/json?place_id={placeId}&fields=photos&key={apiKey}";
                var detailsResponse = await _httpClient.GetStringAsync(detailsUrl);
                using var detailsDoc = JsonDocument.Parse(detailsResponse);

                if (!detailsDoc.RootElement.GetProperty("result").TryGetProperty("photos", out var photos))
                {
                    _logger.LogInformation($"No photos found for place ID: {placeId}");
                    return new List<string>();
                }

                var uploadedUrls = new List<string>();
                var containerClient = _blobServiceClient.GetBlobContainerClient("business-photos");
                await containerClient.CreateIfNotExistsAsync();

                // 3. Download & Upload up to 5 photos
                int count = 0;
                foreach (var photo in photos.EnumerateArray())
                {
                    if (count >= 5) break;
                    string photoRef = photo.GetProperty("photo_reference").GetString()!;
                    
                    var photoUrl = $"https://maps.googleapis.com/maps/api/place/photo?maxwidth=800&photoreference={photoRef}&key={apiKey}";
                    
                    try
                    {
                        var imageBytes = await _httpClient.GetByteArrayAsync(photoUrl);
                        string blobName = $"{businessId}/photo-{count}.jpg";
                        var blobClient = containerClient.GetBlobClient(blobName);
                        
                        using var ms = new System.IO.MemoryStream(imageBytes);
                        await blobClient.UploadAsync(ms, overwrite: true);
                        
                        uploadedUrls.Add(blobClient.Uri.ToString());
                        count++;
                    }
                    catch (Exception imgEx)
                    {
                        _logger.LogWarning($"Failed to download/upload photo {count} for {name}: {imgEx.Message}");
                    }
                }

                return uploadedUrls;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enriching business with Google Places.");
                return new List<string>();
            }
        }
    }
}
