using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using GUNRPG.WebClient;
using GUNRPG.WebClient.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents();
builder.Services.AddScoped<NodeConnectionService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ConnectionStateService>();
builder.Services.AddScoped<BrowserOfflineStore>();
builder.Services.AddScoped<BrowserCombatSessionStore>();
builder.Services.AddScoped<OfflineGameplayService>();
builder.Services.AddScoped<OfflineSyncService>();
builder.Services.AddScoped(sp => new HttpClient());
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<OperatorService>();
builder.Services.AddScoped<MissionService>();

await builder.Build().RunAsync();
