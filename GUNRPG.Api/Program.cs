using System.Text.Json.Serialization;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
using GUNRPG.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCombatSessionStore(builder.Configuration);
builder.Services.AddSingleton<CombatSessionService>();
builder.Services.AddSingleton<OperatorService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
