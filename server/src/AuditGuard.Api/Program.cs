using System;
using AuditGuard.Api.Hubs;
using AuditGuard.Application.Interfaces;
using AuditGuard.Application.Services;
using AuditGuard.Infrastructure.Clients;
using AuditGuard.Infrastructure.Persistence;
using AuditGuard.Infrastructure.Services;
using AuditGuard.Api.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Database Context PostgreSQL + pgvector
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=auditguard_db;Username=audituser;Password=auditpass";

builder.Services.AddDbContext<AuditDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseVector();
    });
});

// Đăng ký Channel Task Queue & Hosted Service Worker bất đồng bộ
builder.Services.AddSingleton<IAuditTaskQueue, AuditTaskQueue>();
builder.Services.AddHostedService<AuditProcessingWorker>();

// Đăng ký Services & Clients
builder.Services.AddHttpClient<IAiEngineClient, AiEngineClient>();
builder.Services.AddScoped<IAuditEngine, AuditEngine>();
builder.Services.AddScoped<IDisputeEmailService, DisputeEmailService>();

// Cấu hình SignalR và Controllers
builder.Services.AddSignalR();
builder.Services.AddControllers();

// Cấu hình CORS mở rộng cho Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AuditGuard Core API", Version = "v1" });
});

var app = builder.Build();

// Tự động kiểm tra và khởi tạo schema Database nếu kết nối được
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    try
    {
        db.Database.EnsureCreated();
        Console.WriteLine("[Database] Khởi tạo kết nối và đồng bộ schema PostgreSQL thành công.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Database Warning] Chưa kết nối được DB (sẽ dùng cache/offline): {ex.Message}");
    }
}

// Cấu hình Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthorization();

app.MapControllers();
app.MapHub<AuditHub>("/hubs/audit");

app.Run();
