namespace MeetAgain.Server.Services
{
    /// <summary>API version of CurrentUserAccessor: reads the caller from the validated Firebase ID token.</summary>
    public class CurrentUserAccessor
    {
        private readonly IHttpContextAccessor _http;

        public CurrentUserAccessor(IHttpContextAccessor http) => _http = http;

        public Task<(string? Uid, string? Email)> GetUserAsync()
        {
            var user = _http.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return Task.FromResult<(string? Uid, string? Email)>((null, null));

            var uid   = user.FindFirst("user_id")?.Value ?? user.FindFirst("sub")?.Value;
            var email = user.FindFirst("email")?.Value;
            return Task.FromResult<(string? Uid, string? Email)>((uid, email));
        }
    }

    /// <summary>API version of AuthService: only exposes the caller's user id, which is all the services need.</summary>
    public class AuthService
    {
        private readonly IHttpContextAccessor _http;

        public AuthService(IHttpContextAccessor http) => _http = http;

        public string? UserId =>
            _http.HttpContext?.User.FindFirst("user_id")?.Value
            ?? _http.HttpContext?.User.FindFirst("sub")?.Value;
    }
}
