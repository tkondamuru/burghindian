using System.Threading.Tasks;

namespace api.Services
{
    public interface IAiService
    {
        Task<AgentResponseDto> ProcessIntentAsync(string text, string? base64Image, string targetIntent);
        Task<string> SaveEventAsync(EventDto eventData, long telegramUserId, string submitterEmail);
        Task<string> SaveBusinessAsync(BusinessDto businessData, long telegramUserId, string submitterEmail);
        Task<string> UploadImageAsync(byte[] imageBytes, string fileName, string containerName);
        Task EnrichBusinessAsync(BusinessDto businessData);
    }
}
