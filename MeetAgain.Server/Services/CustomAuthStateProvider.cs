using System.Security.Claims;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MeetAgain.Server.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private const string TokenKey = "authToken";
private readonly ProtectedSessionStorage _storage;
private string? _token;

public CustomAuthStateProvider(ProtectedSessionStorage storage)
{
    _storage = storage;
}


        public async Task SetTokenAsync(string? token)
        {
            _token = token;

            if (string.IsNullOrWhiteSpace(token))
            {
                // clear from local storage
                await _storage.DeleteAsync(TokenKey);
            }
            else
            {
                // save to local storage
                await _storage.SetAsync(TokenKey, token);
            }

            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                // if we don't have a token in memory, try to load it from local storage
                if (string.IsNullOrWhiteSpace(_token))
                {
                    var stored = await _storage.GetAsync<string>(TokenKey);
                    if (stored.Success && !string.IsNullOrWhiteSpace(stored.Value))
                    {
                        _token = stored.Value;
                    }
                }
                Console.WriteLine($"AuthStateProvider: token is {(string.IsNullOrWhiteSpace(_token) ? "null/empty" : "present")}");
                if (string.IsNullOrWhiteSpace(_token))
                {
                    // no token anywhere → anonymous user
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
                }

                // validate token with Firebase
                var decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(_token);

                decoded.Claims.TryGetValue("email", out var emailObj);
                string email = emailObj?.ToString() ?? string.Empty;

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, decoded.Uid ?? string.Empty),
                    new Claim(ClaimTypes.Email, email)
                };

                var identity = new ClaimsIdentity(claims, "firebase");
                var principal = new ClaimsPrincipal(identity);

                return new AuthenticationState(principal);
            }
            catch
            {
                // token invalid → clear it and treat as anonymous
                await _storage.DeleteAsync(TokenKey);
                _token = null;
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }
    }
}
