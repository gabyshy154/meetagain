using MeetAgain.Server.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MeetAgain.Server.Services
{
    public class GroupService
    {
        private readonly FirestoreService _fs;
        private readonly AuthService _auth;

        public GroupService(FirestoreService fs, AuthService auth)
        {
            _fs = fs;
            _auth = auth;
        }

        public async Task<string> CreateGroupAsync(CreateGroupModel model)
        {
            var userId = _auth.UserId;
            if (userId == null) return string.Empty;

            var group = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                OwnerId = userId,
                Name = model.Name,
                Description = model.Description,
                CreatedAt = DateTime.UtcNow.ToString("o")
            };

            await _fs.CreateGroupAsync(group);

            foreach (var memberId in model.InitialMemberIds)
            {
                await AddMemberAsync(group.Id, memberId);
            }

            return group.Id;
        }

        public async Task<List<GroupDto>> GetMyGroupsAsync()
        {
            var userId = _auth.UserId;
            if (userId == null) return new();

            var groups = await _fs.GetGroupsByOwnerAsync(userId);

            var result = new List<GroupDto>();
            foreach (var g in groups)
            {
                var members = await _fs.GetGroupMembersAsync(g.Id);
                result.Add(new GroupDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    Description = g.Description,
                    MemberCount = members.Count,
                    IsOwner = g.OwnerId == userId,
                    OwnerId = g.OwnerId,
                    OwnerName = g.OwnerId == userId ? "You" : g.OwnerId
                });
            }

            return result;
        }

        public async Task<GroupDto?> GetGroupDetailAsync(string groupId)
        {
            var userId = _auth.UserId;
            var group = await _fs.GetGroupAsync(groupId);
            if (group == null) return null;

            var members = await _fs.GetGroupMembersAsync(groupId);

            var memberDtos = members.Select(m => new GroupMemberDto
            {
                UserId = m.UserId,
                Name = m.UserId,  // replace with user lookup if available
                Email = string.Empty
            }).ToList();

            return new GroupDto
            {
                Id = group.Id,
                Name = group.Name,
                Description = group.Description,
                MemberCount = members.Count,
                IsOwner = group.OwnerId == userId,
                OwnerId = group.OwnerId,
                OwnerName = group.OwnerId == userId ? "You" : group.OwnerId,
                Members = memberDtos
            };
        }

        public async Task<bool> AddMembersToGroupAsync(string groupId, List<string> memberIds)
        {
            try
            {
                foreach (var memberId in memberIds)
                {
                    await AddMemberAsync(groupId, memberId);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RemoveMemberAsync(string groupId, string memberId)
        {
            try
            {
                await _fs.RemoveMemberAsync(groupId, memberId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteGroupAsync(string groupId)
        {
            try
            {
                await _fs.DeleteGroupAsync(groupId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task AddMemberAsync(string groupId, string userToAdd)
        {
            var member = new GroupMember
            {
                UserId = userToAdd,
                AddedAt = DateTime.UtcNow.ToString("o")
            };

            await _fs.AddMemberAsync(groupId, member);
        }

        public Task<List<GroupMember>> GetMembersAsync(string groupId)
        {
            return _fs.GetGroupMembersAsync(groupId);
        }
    }
}