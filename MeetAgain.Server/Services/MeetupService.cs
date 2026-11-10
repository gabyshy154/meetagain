using MeetAgain.Server.Models;
using MeetAgain.Shared.Contracts;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Web-side client for meetups (Stage C6). Firestore access, ownership checks,
    /// invites and notifications now run in MeetAgain.Api. This class keeps the same
    /// method names and signatures so the pages don't change.
    /// </summary>
    public class MeetupService
    {
        private readonly ApiClient _api;

        public MeetupService(ApiClient api)
        {
            _api = api;
        }

        public async Task<bool> CreateMeetupAsync(CreateMeetupModel model, List<string>? invitedFriendIds = null)
        {
            if (model == null) return false;
            return await _api.SendAsync<bool>(HttpMethod.Post, "api/meetups",
                new CreateMeetupRequest { Model = model, InvitedFriendIds = invitedFriendIds });
        }

        public async Task<bool> RespondToMeetupAsync(string meetupId, string status)
        {
            if (string.IsNullOrWhiteSpace(meetupId) || string.IsNullOrWhiteSpace(status)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Post,
                $"api/meetups/{Uri.EscapeDataString(meetupId)}/respond",
                new RespondRequest { Status = status });
        }

        public async Task<List<MeetupDto>> GetMyMeetupsAsync()
        {
            return await _api.GetAsync<List<MeetupDto>>("api/meetups")
                   ?? new List<MeetupDto>();
        }

        public async Task<MeetupDetailDto?> GetMeetupDetailAsync(string meetupId)
        {
            if (string.IsNullOrWhiteSpace(meetupId)) return null;
            return await _api.GetAsync<MeetupDetailDto>(
                $"api/meetups/{Uri.EscapeDataString(meetupId)}/detail");
        }

        // NEW: the raw meetup record, used by the detail/edit pages in the next stage
        // instead of reading Firestore directly. Returns null if it doesn't exist
        // or you aren't the creator/an invitee.
        public async Task<Meetup?> GetMeetupByIdAsync(string meetupId)
        {
            if (string.IsNullOrWhiteSpace(meetupId)) return null;
            return await _api.GetAsync<Meetup>($"api/meetups/{Uri.EscapeDataString(meetupId)}");
        }

        public async Task<bool> DeleteMeetupAsync(string meetupId)
        {
            if (string.IsNullOrWhiteSpace(meetupId)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Delete,
                $"api/meetups/{Uri.EscapeDataString(meetupId)}");
        }

        public async Task<bool> UpdateMeetupAsync(Meetup meetup)
        {
            if (meetup == null || string.IsNullOrWhiteSpace(meetup.Id)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Put,
                $"api/meetups/{Uri.EscapeDataString(meetup.Id)}", meetup);
        }
    }
}
