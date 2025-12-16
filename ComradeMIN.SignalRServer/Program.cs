using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Конфигурация
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Настройка CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("RadminVPNPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .WithExposedHeaders("X-Connection-Id");
    });
});

// Добавляем SignalR с обработкой больших сообщений
builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.EnableDetailedErrors = true;
    hubOptions.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
    hubOptions.StreamBufferCapacity = 10;
    hubOptions.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(30);
    hubOptions.HandshakeTimeout = TimeSpan.FromSeconds(30);
}).AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.WriteIndented = false;
});

// Добавляем контроллеры (если понадобятся)
builder.Services.AddControllers();

// Добавляем логирование
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddDebug();
    logging.SetMinimumLevel(LogLevel.Information);
});

var app = builder.Build();

// Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

// CORS должен быть перед UseEndpoints
app.UseCors("RadminVPNPolicy");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Chat hub
app.MapHub<ChatHub>("/chatHub");

// Fallback для проверки работы сервера
app.MapGet("/", () => "ComradeMIN SignalR Server is running!");

app.Run("http://0.0.0.0:5000");