using MeetAgain.Server.Models;
using MeetAgain.Shared.Contracts;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Web-side client for friends (Stage C4). All Firestore work, friend-request
    /// rules and notifications now run in MeetAgain.Api. This class keeps the same
    /// method names and signatures so the pages don't change.
    /// </summary>
    public class FriendService
    {
        private readonly ApiClient _api;

        public FriendService(ApiClient api)
        {
            _api = api;
        }

        public async Task<List<Friend>> GetFriendsAsync()
        {
            return await _api.GetAsync<List<Friend>>("api/friends")
                   ?? new List<Friend>();
        }

        public async Task<List<FriendRequest>> GetFriendRequestsAsync()
        {
            return await _api.GetAsync<List<FriendRequest>>("api/friends/requests")
                   ?? new List<FriendRequest>();
        }

        public async Task<bool> SendFriendRequestAsync(string recipientEmail)
        {
            if (string.IsNullOrWhiteSpace(recipientEmail)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Post, "api/friends/requests",
                new SendFriendRequestRequest { Email = recipientEmail.Trim() });
        }

        public async Task<bool> AcceptFriendRequestAsync(string fromUserId)
        {
            if (string.IsNullOrWhiteSpace(fromUserId)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Post,
                $"api/friends/requests/{Uri.EscapeDataString(fromUserId)}/accept");
        }

        public async Task<bool> RejectFriendRequestAsync(string fromUserId)
        {
            if (string.IsNullOrWhiteSpace(fromUserId)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Post,
                $"api/friends/requests/{Uri.EscapeDataString(fromUserId)}/reject");
        }

        public async Task<bool> RemoveFriendAsync(string friendId)
        {
            if (string.IsNullOrWhiteSpace(friendId)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Delete,
                $"api/friends/{Uri.EscapeDataString(friendId)}");
        }

        // Returns null when no user has that email (the API answers 404).
        public async Task<AppUser?> SearchUserByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            return await _api.GetAsync<AppUser>(
                $"api/friends/search?email={Uri.EscapeDataString(email.Trim())}");
        }
    }
}
