var builder = WebApplication.CreateBuilder(args);

// Настройка CORS - разрешаем все IP из RadminVPN
builder.Services.AddCors(options =>
{
    // Замените AllowAnyOrigin() на более безопасную конфигурацию
    options.AddPolicy("RadminVPNPolicy", policy =>
    {
        // Получите реальные IP из Radmin VPN
        string[] allowedOrigins = new[]
        {
        "http://26.19.50.66:5000",   // Ваш текущий IP
        "http://localhost:5000",      // Для локального тестирования
        "http://192.168.1.100:5000",  // Пример другого клиента
    };

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSignalR();

var app = builder.Build();

app.UseCors("RadminVPNPolicy");
app.UseRouting();

app.MapHub<ChatHub>("/chatHub");
app.MapGet("/", () => "ComradeMIN SignalR Server is running!");

app.Run("http://0.0.0.0:5000");  // Слушаем на всех интерфейсах