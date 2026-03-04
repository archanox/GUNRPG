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
builder.Services.AddScoped(sp => new HttpClient());
builder.Services.AddScoped<ApiClient>();

await builder.Build().RunAsync();
