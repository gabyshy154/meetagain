using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Services
{
    public class GroupService
    {
        private readonly FirestoreDb _db;
        private readonly AuthService _auth;
        private readonly CurrentUserAccessor _currentUser;

        public GroupService(FirestoreDb db, AuthService auth, CurrentUserAccessor currentUser)
        {
            _db = db;
            _auth = auth;
            _currentUser = currentUser;
        }

        // CREATE GROUP
        public async Task<string?> CreateGroupAsync(CreateGroupModel model)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId)) return null;

            try
            {
                var userDoc = await _db.Collection("users").Document(currentUserId).GetSnapshotAsync();
                if (!userDoc.Exists) return null;

                var userData = userDoc.ToDictionary();
                var ownerName = userData.ContainsKey("DisplayName") ? userData["DisplayName"]?.ToString() ?? "" : "";

                var groupId = Guid.NewGuid().ToString("N");

                var group = new Group
                {
                    Id = groupId,
                    OwnerId = currentUserId,
                    OwnerName = ownerName,
                    Name = model.Name,
                    Description = model.Description,
                    MemberCount = model.InitialMemberIds?.Count ?? 0,
                    CreatedAt = DateTime.UtcNow.ToString("o")
                };

                await _db.Collection("groups").Document(groupId).SetAsync(group);
                Console.WriteLine($"✅ Group created: {groupId}");

                if (model.InitialMemberIds != null && model.InitialMemberIds.Count > 0)
                {
                    await AddMembersToGroupAsync(groupId, model.InitialMemberIds);
                }

                return groupId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error creating group: {ex.Message}");
                return null;
            }
        }

        // GET MY GROUPS - FIXED FOR HARD RELOAD
        public async Task<List<GroupDto>> GetMyGroupsAsync()
        {
            // CRITICAL FIX: Always wait for authentication first
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            
            // Fallback to AuthService
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId))
            {
                Console.WriteLine("❌ GetMyGroups: User not authenticated (waiting for auth state)");
                return new List<GroupDto>();
            }

            try
            {
                Console.WriteLine($"👥 Getting groups for user: {currentUserId}");
                
                var groups = new List<GroupDto>();
                var allGroupsSnapshot = await _db.Collection("groups").GetSnapshotAsync();

                Console.WriteLine($"  └─ Found {allGroupsSnapshot.Count} total group(s) in Firestore");

                foreach (var groupDoc in allGroupsSnapshot.Documents)
                {
                    var group = groupDoc.ConvertTo<Group>();

                    // Check if user is the owner
                    if (group.OwnerId == currentUserId)
                    {
                        groups.Add(new GroupDto
                        {
                            Id = group.Id,
                            OwnerId = group.OwnerId,
                            OwnerName = group.OwnerName,
                            Name = group.Name,
                            Description = group.Description,
                            MemberCount = group.MemberCount,
                            CreatedAt = group.CreatedAt,
                            IsOwner = true
                        });
                        Console.WriteLine($"  └─ Added owned group: {group.Name}");
                        continue;
                    }

                    // Check if user is a member
                    var memberDoc = await _db.Collection("groups")
                        .Document(group.Id)
                        .Collection("members")
                        .Document(currentUserId)
                        .GetSnapshotAsync();

                    if (memberDoc.Exists)
                    {
                        groups.Add(new GroupDto
                        {
                            Id = group.Id,
                            OwnerId = group.OwnerId,
                            OwnerName = group.OwnerName,
                            Name = group.Name,
                            Description = group.Description,
                            MemberCount = group.MemberCount,
                            CreatedAt = group.CreatedAt,
                            IsOwner = false
                        });
                        Console.WriteLine($"  └─ Added member group: {group.Name}");
                    }
                }

                Console.WriteLine($"✅ Returning {groups.Count} group(s)");
                return groups.OrderBy(g => g.Name).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting groups: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<GroupDto>();
            }
        }

        // GET GROUP DETAILS
        public async Task<GroupDto?> GetGroupDetailAsync(string groupId)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId)) return null;

            try
            {
                var groupDoc = await _db.Collection("groups").Document(groupId).GetSnapshotAsync();
                if (!groupDoc.Exists) return null;

                var group = groupDoc.ConvertTo<Group>();

                var membersSnapshot = await _db.Collection("groups")
                    .Document(groupId)
                    .Collection("members")
                    .GetSnapshotAsync();

                var members = new List<GroupMember>();
                foreach (var doc in membersSnapshot.Documents)
                {
                    members.Add(doc.ConvertTo<GroupMember>());
                }

                return new GroupDto
                {
                    Id = group.Id,
                    OwnerId = group.OwnerId,
                    OwnerName = group.OwnerName,
                    Name = group.Name,
                    Description = group.Description,
                    MemberCount = group.MemberCount,
                    CreatedAt = group.CreatedAt,
                    IsOwner = group.OwnerId == currentUserId,
                    Members = members.OrderBy(m => m.Name).ToList()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting group detail: {ex.Message}");
                return null;
            }
        }

        // GET GROUP BY ID (used by CreateMeetup)
        public async Task<GroupDto?> GetGroupByIdAsync(string groupId)
        {
            return await GetGroupDetailAsync(groupId);
        }

        // ADD MEMBERS TO GROUP
        public async Task<bool> AddMembersToGroupAsync(string groupId, List<string> friendIds)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId)) return false;

            try
            {
                var groupDoc = await _db.Collection("groups").Document(groupId).GetSnapshotAsync();
                if (!groupDoc.Exists) return false;

                var group = groupDoc.ConvertTo<Group>();
                if (group.OwnerId != currentUserId) return false;

                var batch = _db.StartBatch();
                var addedCount = 0;

                foreach (var friendId in friendIds)
                {
                    var existingMember = await _db.Collection("groups")
                        .Document(groupId)
                        .Collection("members")
                        .Document(friendId)
                        .GetSnapshotAsync();

                    if (existingMember.Exists) continue;

                    var friendDoc = await _db.Collection("users").Document(friendId).GetSnapshotAsync();
                    if (!friendDoc.Exists) continue;

                    var friendData = friendDoc.ToDictionary();
                    var member = new GroupMember
                    {
                        UserId = friendId,
                        Name = friendData.ContainsKey("DisplayName") ? friendData["DisplayName"]?.ToString() ?? "" : "",
                        Email = friendData.ContainsKey("Email") ? friendData["Email"]?.ToString() ?? "" : "",
                        AddedAt = DateTime.UtcNow.ToString("o"),
                        AddedBy = currentUserId
                    };

                    batch.Set(_db.Collection("groups").Document(groupId).Collection("members").Document(friendId), member);
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    batch.Update(_db.Collection("groups").Document(groupId), new Dictionary<string, object>
                    {
                        { "MemberCount", group.MemberCount + addedCount }
                    });
                }

                await batch.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error adding members: {ex.Message}");
                return false;
            }
        }

        // REMOVE MEMBER
        public async Task<bool> RemoveMemberAsync(string groupId, string memberId)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId)) return false;

            try
            {
                var groupDoc = await _db.Collection("groups").Document(groupId).GetSnapshotAsync();
                if (!groupDoc.Exists) return false;

                var group = groupDoc.ConvertTo<Group>();
                if (group.OwnerId != currentUserId) return false;

                var batch = _db.StartBatch();
                batch.Delete(_db.Collection("groups").Document(groupId).Collection("members").Document(memberId));
                batch.Update(_db.Collection("groups").Document(groupId), new Dictionary<string, object>
                {
                    { "MemberCount", Math.Max(0, group.MemberCount - 1) }
                });

                await batch.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error removing member: {ex.Message}");
                return false;
            }
        }

        // DELETE GROUP
        public async Task<bool> DeleteGroupAsync(string groupId)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId)) return false;

            try
            {
                var groupDoc = await _db.Collection("groups").Document(groupId).GetSnapshotAsync();
                if (!groupDoc.Exists) return false;

                var group = groupDoc.ConvertTo<Group>();
                if (group.OwnerId != currentUserId) return false;

                var membersSnapshot = await _db.Collection("groups")
                    .Document(groupId)
                    .Collection("members")
                    .GetSnapshotAsync();

                var batch = _db.StartBatch();
                foreach (var doc in membersSnapshot.Documents)
                {
                    batch.Delete(doc.Reference);
                }

                batch.Delete(_db.Collection("groups").Document(groupId));
                await batch.CommitAsync();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error deleting group: {ex.Message}");
                return false;
            }
        }

        // GET GROUP MEMBER IDS
        public async Task<List<string>> GetGroupMemberIdsAsync(string groupId)
        {
            try
            {
                Console.WriteLine($"Getting member IDs for group: {groupId}");
                
                var membersSnapshot = await _db.Collection("groups")
                    .Document(groupId)
                    .Collection("members")
                    .GetSnapshotAsync();

                var memberIds = membersSnapshot.Documents.Select(d => d.Id).ToList();
                Console.WriteLine($"  └─ Found {memberIds.Count} member(s)");
                
                return memberIds;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting group member IDs: {ex.Message}");
                return new List<string>();
            }
        }
    }
}