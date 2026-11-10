using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Calls the MeetAgain API on behalf of the signed-in user by attaching their
    /// Firebase ID token. Failures are logged and returned as null/false so a page
    /// never crashes just because the API is unreachable.
    /// Use relative paths with NO leading slash, e.g. "api/notifications".
    /// </summary>
    public class ApiClient
    {
        private readonly HttpClient _http;
        private readonly CustomAuthStateProvider _auth;

        public ApiClient(HttpClient http, CustomAuthStateProvider auth)
        {
            _http = http;
            _auth = auth;
        }

        private async Task<HttpResponseMessage?> RawAsync(HttpMethod method, string path, object? body)
        {
            try
            {
                if (string.IsNullOrEmpty(_auth.Token))
                    await _auth.GetAuthenticationStateAsync();   // loads the token from browser storage

                var token = _auth.Token;
                if (string.IsNullOrEmpty(token)) return null;     // not signed in

                using var req = new HttpRequestMessage(method, path);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (body != null) req.Content = JsonContent.Create(body);

                return await _http.SendAsync(req);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"API call failed ({method} {path}): {ex.Message}");
                return null;
            }
        }

        public async Task<T?> GetAsync<T>(string path)
        {
            using var res = await RawAsync(HttpMethod.Get, path, null);
            if (res == null) return default;
            if (!res.IsSuccessStatusCode)
            {
                Console.WriteLine($"API GET {path} -> {(int)res.StatusCode}");
                return default;
            }
            return await res.Content.ReadFromJsonAsync<T>();
        }

        public async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null)
        {
            using var res = await RawAsync(method, path, body);
            if (res == null) return default;
            if (!res.IsSuccessStatusCode)
            {
                Console.WriteLine($"API {method} {path} -> {(int)res.StatusCode}");
                return default;
            }
            return await res.Content.ReadFromJsonAsync<T>();
        }

        /// <summary>For calls with no response body. Returns true on a 2xx status.</summary>
        public async Task<bool> SendAsync(HttpMethod method, string path, object? body = null)
        {
            using var res = await RawAsync(method, path, body);
            if (res == null) return false;
            if (!res.IsSuccessStatusCode)
                Console.WriteLine($"API {method} {path} -> {(int)res.StatusCode}");
            return res.IsSuccessStatusCode;
        }
    }
}
