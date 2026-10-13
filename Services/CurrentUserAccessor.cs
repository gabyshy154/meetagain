using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace MeetAgain.Server.Services
{
    public class CurrentUserAccessor
    {
        private readonly AuthenticationStateProvider _authStateProvider;

        public CurrentUserAccessor(AuthenticationStateProvider authStateProvider)
        {
            _authStateProvider = authStateProvider;
        }

        public async Task<(string? Uid, string? Email)> GetUserAsync()
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            if (user?.Identity?.IsAuthenticated != true)
                return (null, null);

            var uid = user.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var email = user.FindFirst(c => c.Type == ClaimTypes.Email)?.Value;

            return (uid, email);
        }
    }
}
