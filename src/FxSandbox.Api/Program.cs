using System.Text.Json.Serialization;
using FxSandbox.Api;
using FxSandbox.Simulation;
using Microsoft.AspNetCore.Diagnostics;

const string UiCorsPolicy = "ui";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
// Malformed request bodies (bad JSON, unknown enum names) surface as BadHttpRequestException: report their 4xx
// status; anything else stays a ProblemDetails 500.
builder.Services.Configure<ExceptionHandlerOptions>(o =>
    o.StatusCodeSelector = ex => ex is BadHttpRequestException bad
        ? bad.StatusCode
        : StatusCodes.Status500InternalServerError);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddCors(o => o.AddPolicy(UiCorsPolicy, p => p
    .WithOrigins("http://localhost:5173", "https://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials())); // SignalR negotiate sends credentials

builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddSandboxSimulation(builder.Configuration);
builder.Services.AddHostedService<SandboxHubPublisher>();

var app = builder.Build();

app.UseExceptionHandler();
// Gives bare error statuses (e.g. a 404 for an unknown route) a ProblemDetails body.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(UiCorsPolicy);
app.UseStaticFiles();

app.MapSandboxEndpoints();
app.MapHub<SandboxHub>(SandboxHub.Route);

app.MapFallbackToFile("index.html");

app.Run();

// Exposed so WebApplicationFactory<Program> can host the app in tests.
public partial class Program;
