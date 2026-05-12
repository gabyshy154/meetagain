using MeetAgain.Server.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MeetAgain.Server.Services
{
    public class MeetupService
    {
        private readonly FirestoreService _fs;
        private readonly AuthService _auth;

        public MeetupService(FirestoreService fs, AuthService auth)
        {
            _fs = fs;
            _auth = auth;
        }

        // ------------------------------------------------------
        // CREATE MEETUP
        // ------------------------------------------------------
        public async Task<bool> CreateMeetupAsync(CreateMeetupModel model)
        {
            var currentUser = await _auth.GetCurrentUserAsync();
            if (currentUser == null || string.IsNullOrWhiteSpace(currentUser.Uid))
                return false;

            var eventDateTime = model.Date.Date
                                    .Add(model.Time.ToTimeSpan())
                                    .ToUniversalTime();

            var meetup = new Meetup
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatorUserId = currentUser.Uid,
                CreatorName = currentUser.DisplayName ?? currentUser.Email ?? "",
                Title = model.Title,
                Description = model.Description,
                Location = model.Location,
                EventDateTime = eventDateTime,
                CreatedAt = DateTime.UtcNow
            };

            await _fs.CreateMeetupAsync(meetup);
            return true;
        }

        // ------------------------------------------------------
        // GET MY MEETUPS
        // ------------------------------------------------------
        public async Task<List<MeetupDto>> GetMyMeetupsAsync()
        {
            var currentUser = await _auth.GetCurrentUserAsync();
            if (currentUser == null || string.IsNullOrWhiteSpace(currentUser.Uid))
                return new List<MeetupDto>();

            return await _fs.GetUserMeetupsAsync(currentUser.Uid);
        }

        // ------------------------------------------------------
        // DELETE MEETUP
        // ------------------------------------------------------
        public async Task<bool> DeleteMeetupAsync(string meetupId)
        {
            if (string.IsNullOrWhiteSpace(meetupId))
                return false;

            var meetup = await _fs.GetMeetupByIdAsync(meetupId);
            if (meetup == null)
                return false;

            var currentUser = await _auth.GetCurrentUserAsync();
            if (currentUser == null || meetup.CreatorUserId != currentUser.Uid)
                return false;

            await _fs.DeleteMeetupAsync(meetup.CreatorUserId, meetup.Id);
            return true;
        }

        // ------------------------------------------------------
        // UPDATE MEETUP
        // ------------------------------------------------------
        public async Task<bool> UpdateMeetupAsync(Meetup meetup)
        {
            if (meetup == null || string.IsNullOrWhiteSpace(meetup.Id))
                return false;

            var currentUser = await _auth.GetCurrentUserAsync();
            if (currentUser == null || meetup.CreatorUserId != currentUser.Uid)
                return false;

            await _fs.UpdateMeetupAsync(meetup);
            return true;
        }
    }
}
