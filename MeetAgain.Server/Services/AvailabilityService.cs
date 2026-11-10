using MeetAgain.Shared.Contracts;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Web-side client for availability (Stage C3). The Firestore access and the
    /// "find best time" scoring now run in MeetAgain.Api; this class keeps the same
    /// method names and signatures so the pages don't change.
    /// </summary>
    public class AvailabilityService
    {
        private readonly ApiClient _api;

        public AvailabilityService(ApiClient api)
        {
            _api = api;
        }

        // The API only lets you read your own settings. If the call fails,
        // return empty settings (same shape the page gets for a brand-new user).
        public async Task<UserAvailability> GetUserAvailabilityAsync(string userId)
        {
            var result = await _api.GetAsync<UserAvailability>(
                $"api/availability/{Uri.EscapeDataString(userId)}");
            return result ?? new UserAvailability { UserId = userId };
        }

        // Throws on failure (like the old Firestore version did) so the page can
        // show its "save failed" message instead of pretending it worked.
        public async Task SaveUserAvailabilityAsync(UserAvailability availability)
        {
            var ok = await _api.SendAsync(HttpMethod.Put, "api/availability", availability);
            if (!ok)
                throw new InvalidOperationException("Could not save availability. Please try again.");
        }

        // NOTE: participantUserIds should already include the meetup creator's UID
        public async Task<List<SuggestedTimeSlot>> FindBestMeetupTimesAsync(
            List<string> participantUserIds,
            DateTime startDate,
            DateTime endDate,
            int durationMinutes = 120,
            int maxSuggestions = 5)
        {
            var request = new BestTimesRequest
            {
                ParticipantUserIds = participantUserIds,
                StartDate = startDate,
                EndDate = endDate,
                DurationMinutes = durationMinutes,
                MaxSuggestions = maxSuggestions
            };

            return await _api.SendAsync<List<SuggestedTimeSlot>>(
                       HttpMethod.Post, "api/availability/best-times", request)
                   ?? new List<SuggestedTimeSlot>();
        }
    }
}
