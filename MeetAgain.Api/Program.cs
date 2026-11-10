using System.Security.Claims;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// Firebase settings
// AWS:    base64 of the service-account JSON in FIREBASE_CREDENTIALS_B64
// Laptop: file named by Firebase:CredentialsFile (appsettings.json)
// The credentials now live ONLY on this API server, never on the web server.
// ------------------------------------------------------
var credentialsB64  = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_B64");
var credentialsJson = !string.IsNullOrWhiteSpace(credentialsB64)
    ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(credentialsB64.Trim()))
    : Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS");
var credentialsPath = builder.Configuration["Firebase:CredentialsFile"];
var projectId       = builder.Configuration["Firebase:ProjectId"];

if (string.IsNullOrWhiteSpace(credentialsJson) && string.IsNullOrWhiteSpace(credentialsPath))
    throw new Exception("Set FIREBASE_CREDENTIALS_B64 or Firebase:CredentialsFile");
if (string.IsNullOrWhiteSpace(projectId))
    throw new Exception("Missing Firebase:ProjectId");

GoogleCredential googleCred;
if (!string.IsNullOrWhiteSpace(credentialsJson))
{
    googleCred = GoogleCredential.FromJson(credentialsJson);
}
else
{
    using var stream = new FileStream(credentialsPath!, FileMode.Open, FileAccess.Read);
    googleCred = GoogleCredential.FromStream(stream);
}
googleCred = googleCred.CreateScoped("https://www.googleapis.com/auth/cloud-platform");

if (FirebaseApp.DefaultInstance == null)
{
    FirebaseApp.Create(new AppOptions { Credential = googleCred });
}

var firestoreDb = new FirestoreDbBuilder
{
    ProjectId  = projectId,
    Credential = googleCred
}.Build();

builder.Services.AddSingleton(firestoreDb);

// ------------------------------------------------------
// Authentication: every request must carry the user's Firebase ID token
// (Authorization: Bearer <token>). The keys are downloaded from Google automatically.
// ------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://securetoken.google.com/{projectId}";
        options.MapInboundClaims = false;   // keep the original claim names (sub, user_id, email)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer   = true,
            ValidIssuer      = $"https://securetoken.google.com/{projectId}",
            ValidateAudience = true,
            ValidAudience    = projectId,
            ValidateLifetime = true
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddControllers();

// ------------------------------------------------------
// TODO (next step): register the services moved over from the web app, e.g.
//   builder.Services.AddScoped<FirestoreService>();
//   builder.Services.AddScoped<FriendService>();
//   builder.Services.AddScoped<MeetupService>();
//   ... GroupService, NotificationService, AvailabilityService
// ------------------------------------------------------

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Open endpoint for checking that the server is up
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// Smoke test for token validation: returns the signed-in user's id
app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new { uid = user.GetUid() }))
   .RequireAuthorization();

// Every controller endpoint requires a signed-in user
app.MapControllers().RequireAuthorization();

app.Run();

public static class ClaimsExtensions
{
    /// <summary>The Firebase user id of the caller (the same value the web app uses today).</summary>
    public static string GetUid(this ClaimsPrincipal user) =>
        user.FindFirst("user_id")?.Value
        ?? user.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException("No user id in token.");
}
