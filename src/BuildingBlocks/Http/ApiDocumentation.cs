using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace BuildingBlocks.Http;

/// <summary>
/// Swagger dùng chung cho cả ba service để giao diện và cách xác thực giống nhau.
/// Khai báo sẵn bearer token nên nút Authorize hoạt động — dán access token vào là gọi được
/// endpoint có bảo vệ ngay trên trình duyệt, không cần Postman.
/// </summary>
public static class ApiDocumentation
{
    public static IServiceCollection AddApiDocumentation(
        this IServiceCollection services, string title, string description)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo { Title = title, Version = "v1", Description = description });

            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Lấy token từ POST /auth/login của Payments (cổng 8081), dán vào đây.",
            });

            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                }] = [],
            });
        });
        return services;
    }

    /// <summary>
    /// Bật giao diện Swagger tại <c>/swagger</c>.
    /// Đặt <c>Swagger:Enabled=false</c> để tắt — môi trường production thường không muốn phơi
    /// toàn bộ danh sách endpoint ra ngoài.
    /// </summary>
    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        if (app.Configuration.GetValue("Swagger:Enabled", true) is false) return app;

        // Middleware Swagger chạy cho MỌI request và sẽ ném lỗi nếu quên gọi AddApiDocumentation.
        // Không kiểm tra ở đây thì một lần quên đăng ký sẽ làm hỏng cả /health lẫn toàn bộ endpoint,
        // tức là tài liệu API hạ luôn service — cái giá quá đắt cho một tiện ích phụ trợ.
        if (app.Services.GetService<ISwaggerProvider>() is null)
        {
            app.Logger.LogWarning(
                "Bỏ qua Swagger: chưa gọi AddApiDocumentation() lúc đăng ký service.");
            return app;
        }

        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
            o.DocumentTitle = app.Environment.ApplicationName;
            o.DisplayRequestDuration();
        });
        return app;
    }
}
