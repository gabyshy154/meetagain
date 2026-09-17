using FirebaseAdmin.Auth;
using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly FirestoreService _fs;
        private readonly string _apiKey;
        private static readonly HttpClient _http = new();

        public AuthController(FirestoreService fs, IConfiguration config)
        {
            _fs = fs;
            _apiKey = config["Firebase:ApiKey"] ?? "";
        }

        // POST /api/auth/signup
        [HttpPost("signup")]
        public async Task<IActionResult> Signup([FromBody] SignupRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.DisplayName))
                return BadRequest(new { error = "email, password and displayName are required." });

            UserRecord? fbUser = null;
            try
            {
                fbUser = await FirebaseAuth.DefaultInstance.CreateUserAsync(new UserRecordArgs
                {
                    Email = req.Email,
                    Password = req.Password,
                    DisplayName = req.DisplayName
                });
            }
            catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.EmailAlreadyExists)
            {
                return Conflict(new { error = "Email already exists." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "Firebase registration failed: " + ex.Message });
            }

            var user = new AppUser
            {
                Uid = fbUser.Uid,
                Email = fbUser.Email ?? req.Email,
                DisplayName = req.DisplayName,
                CreatedAt = DateTime.UtcNow.ToString("o")
            };

            try
            {
                await _fs.CreateOrUpdateUserAsync(user);
            }
            catch (Exception ex)
            {
                try { await FirebaseAuth.DefaultInstance.DeleteUserAsync(fbUser.Uid); } catch { }
                return StatusCode(500, new { error = "Failed to write user to Firestore: " + ex.Message });
            }

            return CreatedAtAction(nameof(Me), new { userId = user.Uid }, user);
        }

        // POST /api/auth/login — stateless (does NOT touch Blazor ProtectedSessionStorage).
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { error = "email and password are required." });
            if (string.IsNullOrWhiteSpace(_apiKey))
                return StatusCode(500, new { error = "Missing Firebase:ApiKey server configuration." });

            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={_apiKey}";
            var payload = new { email = req.Email, password = req.Password, returnSecureToken = true };

            HttpResponseMessage response;
            string raw;
            try
            {
                response = await _http.PostAsJsonAsync(url, payload);
                raw = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = "Firebase login request failed: " + ex.Message });
            }

            if (!response.IsSuccessStatusCode)
                return Unauthorized(new { error = "Invalid email or password.", details = raw });

            string idToken, localId;
            try
            {
                var json = JsonDocument.Parse(raw).RootElement;
                idToken = json.TryGetProperty("idToken", out var t) ? t.GetString() ?? "" : "";
                localId = json.TryGetProperty("localId", out var l) ? l.GetString() ?? "" : "";
            }
            catch
            {
                return StatusCode(502, new { error = "Unparseable Firebase login response." });
            }

            if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(localId))
                return StatusCode(502, new { error = "idToken or localId missing from Firebase response." });

            var user = await _fs.GetUserAsync(localId);
            if (user == null)
                return NotFound(new { error = "User not found in Firestore." });

            // Client should send this idToken back as: Authorization: Bearer <idToken>
            // or X-User-Id: <uid> for subsequent calls.
            return Ok(new { idToken, user });
        }

        // POST /api/auth/logout — no-op for stateless API (client discards idToken).
        [HttpPost("logout")]
        public IActionResult Logout() => Ok(new { message = "Logged out. Discard idToken client-side." });

        // GET /api/auth/me?userId=xxx  (or Authorization: Bearer <idToken>, or X-User-Id header)
        [HttpGet("me")]
        public async Task<IActionResult> Me([FromQuery] string? userId)
        {
            var (uid, _) = await ApiUserContext.ResolveAsync(HttpContext, userId);
            if (string.IsNullOrWhiteSpace(uid))
                return Unauthorized(new { error = "Missing user identity. Send Authorization: Bearer <FirebaseIdToken>, X-User-Id header, or ?userId= query." });

            var user = await _fs.GetUserAsync(uid);
            if (user == null) return NotFound(new { error = "User not found." });
            return Ok(user);
        }
    }
}
