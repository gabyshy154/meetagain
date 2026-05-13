using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// Load Firebase settings
// ------------------------------------------------------
var credentialsPath = builder.Configuration["Firebase:CredentialsFile"];
var projectId       = builder.Configuration["Firebase:ProjectId"];
var apiKey          = builder.Configuration["Firebase:ApiKey"];

if (string.IsNullOrWhiteSpace(credentialsPath))
    throw new Exception("Missing Firebase:CredentialsFile");
if (string.IsNullOrWhiteSpace(projectId))
    throw new Exception("Missing Firebase:ProjectId");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new Exception("Missing Firebase:ApiKey");

// ------------------------------------------------------
// Initialize Firebase Admin SDK
// ------------------------------------------------------
GoogleCredential googleCred;
using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
{
    googleCred = GoogleCredential.FromStream(stream)
        .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
}

if (FirebaseApp.DefaultInstance == null)
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = googleCred
    });
}

// ------------------------------------------------------
// Firestore
// ------------------------------------------------------
var firestoreDb = new FirestoreDbBuilder
{
    ProjectId  = projectId,
    Credential = googleCred
}.Build();

builder.Services.AddSingleton(firestoreDb);

// ------------------------------------------------------
// Blazor & Authentication
// ------------------------------------------------------
builder.Services.AddRazorPages();

// ✅ AddServerSideBlazor registers ProtectedSessionStorage automatically.
//    We register ProtectedLocalStorage separately for Remember Me support.
//    Do NOT manually register ProtectedSessionStorage — AddServerSideBlazor
//    already does it and a duplicate causes injection conflicts.
builder.Services.AddServerSideBlazor().AddCircuitOptions(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
});

// Registers ProtectedLocalStorage (needed for Remember Me persistence)
builder.Services.AddScoped<ProtectedLocalStorage>();

// Auth state provider wiring
builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<CustomAuthStateProvider>());

builder.Services.AddAuthorizationCore();

// ------------------------------------------------------
// App Services
// ------------------------------------------------------
builder.Services.AddScoped<FirestoreService>();

builder.Services.AddScoped(sp =>
{
    var fs                = sp.GetRequiredService<FirestoreService>();
    var authStateProvider = sp.GetRequiredService<CustomAuthStateProvider>();
    var svc               = new AuthService(fs, apiKey);
    svc.AuthStateProvider = authStateProvider;
    fs.AuthStateProvider  = authStateProvider;
    return svc;
});

builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<FriendService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<MeetupService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AvailabilityService>();

// ------------------------------------------------------
// Build app
// ------------------------------------------------------
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();