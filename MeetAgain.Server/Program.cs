using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// Settings. The web server holds NO Firebase service-account key and does
// not talk to Firestore: all data goes through MeetAgain.Api.
//   Firebase:ApiKey    public Firebase web key (login / sign-up REST calls)
//   Firebase:ProjectId used to check the token's issuer/audience
//   Api:BaseUrl        where MeetAgain.Api lives (on AWS: environment property Api__BaseUrl)
// ------------------------------------------------------
var apiKey     = builder.Configuration["Firebase:ApiKey"];
var projectId  = builder.Configuration["Firebase:ProjectId"];
var apiBaseUrl = builder.Configuration["Api:BaseUrl"];

if (string.IsNullOrWhiteSpace(apiKey))
    throw new Exception("Missing Firebase:ApiKey");
if (string.IsNullOrWhiteSpace(projectId))
    throw new Exception("Missing Firebase:ProjectId");
if (string.IsNullOrWhiteSpace(apiBaseUrl))
    throw new Exception("Missing Api:BaseUrl");

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
// MeetAgain API client
// ------------------------------------------------------
builder.Services.AddHttpClient<ApiClient>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(30);
});

// ------------------------------------------------------
// App Services (all talk to the API)
// ------------------------------------------------------
builder.Services.AddScoped(sp =>
{
    var api               = sp.GetRequiredService<ApiClient>();
    var authStateProvider = sp.GetRequiredService<CustomAuthStateProvider>();
    var svc               = new AuthService(api, apiKey);
    svc.AuthStateProvider = authStateProvider;
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
