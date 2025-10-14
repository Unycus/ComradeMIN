var builder = WebApplication.CreateBuilder(args);

// Настройка CORS - разрешаем все IP из RadminVPN
builder.Services.AddCors(options =>
{
    options.AddPolicy("RadminVPNPolicy", policy =>
    {
        policy.AllowAnyOrigin()  // Разрешаем все источники для тестирования
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