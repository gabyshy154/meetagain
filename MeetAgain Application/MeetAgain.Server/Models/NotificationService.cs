using MeetAgain.Server.Models;

namespace MeetAgain.Server.Services
{
    public class NotificationService
    {
        private readonly List<NotificationDto> _notifications = new();

        public Task<List<NotificationDto>> GetMyNotificationsAsync()
        {
            return Task.FromResult(_notifications);
        }

        public Task<int> GetUnreadCountAsync()
        {
            return Task.FromResult(
                _notifications.Count(n => !n.IsRead)
            );
        }

        public Task AddNotificationAsync(NotificationDto notification)
        {
            _notifications.Insert(0, notification);
            return Task.CompletedTask;
        }

        public Task MarkAsReadAsync(string notificationId)
        {
            var notification = _notifications
                .FirstOrDefault(n => n.Id == notificationId);

            if (notification != null)
            {
                notification.IsRead = true;
            }

            return Task.CompletedTask;
        }

        public Task MarkAllAsReadAsync()
        {
            foreach (var notification in _notifications)
            {
                notification.IsRead = true;
            }

            return Task.CompletedTask;
        }

        public Task DeleteNotificationAsync(string notificationId)
        {
            _notifications.RemoveAll(n => n.Id == notificationId);

            return Task.CompletedTask;
        }
    }
}