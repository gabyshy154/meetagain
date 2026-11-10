using MeetAgain.Server.Models;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Web-side client for notifications. Creating notifications (invites, RSVPs,
    /// friend requests...) now happens inside MeetAgain.Api, so this class only
    /// reads and updates the signed-in user's own notifications.
    /// </summary>
    public class NotificationService
    {
        private readonly ApiClient _api;

        public NotificationService(ApiClient api)
        {
            _api = api;
        }

        // Get all notifications for current user
        public async Task<List<NotificationDto>> GetMyNotificationsAsync()
        {
            return await _api.GetAsync<List<NotificationDto>>("api/notifications")
                   ?? new List<NotificationDto>();
        }

        // Get unread notification count
        public async Task<int> GetUnreadCountAsync()
        {
            return await _api.GetAsync<int>("api/notifications/unread-count");
        }

        // Mark notification as read
        public async Task MarkAsReadAsync(string notificationId)
        {
            if (string.IsNullOrWhiteSpace(notificationId)) return;
            await _api.SendAsync(HttpMethod.Post,
                $"api/notifications/{Uri.EscapeDataString(notificationId)}/read");
        }

        // Mark all notifications as read
        public async Task MarkAllAsReadAsync()
        {
            await _api.SendAsync(HttpMethod.Post, "api/notifications/read-all");
        }

        // Delete a notification
        public async Task DeleteNotificationAsync(string notificationId)
        {
            if (string.IsNullOrWhiteSpace(notificationId)) return;
            await _api.SendAsync(HttpMethod.Delete,
                $"api/notifications/{Uri.EscapeDataString(notificationId)}");
        }
    }
}
