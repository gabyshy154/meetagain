using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace MeetAgain.Server.Services
{
    public class CurrentUserAccessor
    {
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public CurrentUserAccessor(
            AuthenticationStateProvider authStateProvider,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _authStateProvider = authStateProvider;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<(string? Uid, string? Email)> GetUserAsync()
        {
            // FIX: prefer stateless HttpContext (API controllers) when available,
            // so API requests don't depend on the Blazor circuit / ProtectedSessionStorage.
            try
            {
                var http = _httpContextAccessor?.HttpContext;
                if (http?.User?.Identity?.IsAuthenticated == true)
                {
                    var uid = http.User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                        ?? http.User.FindFirst("uid")?.Value;
                    var email = http.User.FindFirst(c => c.Type == ClaimTypes.Email)?.Value;
                    if (!string.IsNullOrWhiteSpace(uid))
                        return (uid, email);
                }
            }
            catch
            {
                // ignore and fall back to Blazor auth state
            }

            // Blazor circuit path (may throw outside a circuit when JS interop is
            // unavailable, e.g. inside an API request) — never let it crash callers.
            try
            {
                var authState = await _authStateProvider.GetAuthenticationStateAsync();
                var user = authState.User;

                if (user?.Identity?.IsAuthenticated != true)
                    return (null, null);

                var blazorUid = user.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                var blazorEmail = user.FindFirst(c => c.Type == ClaimTypes.Email)?.Value;

                return (blazorUid, blazorEmail);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
