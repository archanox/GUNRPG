using System.Text.Json.Serialization;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
using GUNRPG.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();

builder.Services.AddCombatSessionStore(builder.Configuration);
builder.Services.AddSingleton<CombatSessionService>(sp =>
{
    var sessionStore = sp.GetRequiredService<ICombatSessionStore>();
    var operatorEventStore = sp.GetRequiredService<IOperatorEventStore>();
    return new CombatSessionService(sessionStore, operatorEventStore);
});
builder.Services.AddSingleton<OperatorService>(sp =>
{
    var exfilService = sp.GetRequiredService<OperatorExfilService>();
    var sessionService = sp.GetRequiredService<CombatSessionService>();
    var eventStore = sp.GetRequiredService<IOperatorEventStore>();
    return new OperatorService(exfilService, sessionService, eventStore);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();

app.Run();
