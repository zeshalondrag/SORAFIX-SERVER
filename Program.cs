using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using sorafix_api.Models;
using sorafix_api.Services;
using System.Security.Claims;
using System.Text;
using Telegram.Bot;
using Resend;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

if (File.Exists(".env"))
{
    Env.Load();
}
builder.Configuration.AddEnvironmentVariables();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<SorafixContext>(options => 
    options.UseNpgsql(connectionString));

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new IPAddressConverter());
    });
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.Configure<ResendClientOptions>(options =>
{
    options.ApiToken = builder.Configuration["Resend:ApiKey"]!;
});
builder.Services.AddHttpClient<IResend, ResendClient>();
builder.Services.AddTransient<IResend, ResendClient>();

builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddMemoryCache();
builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new TelegramBotClient(config["Telegram:Token"]!);
});
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHttpClient<YooKassaService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("https://sorafix.vercel.app")
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials();
    });
});

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new Exception("JWT Key is missing!");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddAuthorization();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "SORAFIX API";
        document.Info.Version = "v1";
        document.Info.Description = "API системы SORAFIX. Используйте JWT Bearer для авторизации.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        var securityScheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Name = "Authorization",
            In = ParameterLocation.Header,
            Scheme = "Bearer",
            BearerFormat = "JWT"
        };

        if (!document.Components.SecuritySchemes.ContainsKey("Bearer"))
        {
            document.Components.SecuritySchemes.Add("Bearer", securityScheme);
        }

        var reference = new OpenApiSecuritySchemeReference("Bearer", document);

        var requirement = new OpenApiSecurityRequirement
        {
            [reference] = new List<string>()
        };

        document.Security = new List<OpenApiSecurityRequirement>
    {
        requirement
    };

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("SORAFIX API Documentation")
            .WithTheme(ScalarTheme.Alternate)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var userIp = context.Connection.RemoteIpAddress?.ToString();

    var dbContext = context.RequestServices.GetRequiredService<SorafixContext>();
    await dbContext.Database.OpenConnectionAsync();

    try
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_user_id', @userId, false)",
            new Npgsql.NpgsqlParameter("userId", userId ?? (object)DBNull.Value)
        );

        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_user_ip', @userIp, false)",
            new Npgsql.NpgsqlParameter("userIp", userIp ?? (object)DBNull.Value)
        );

        await next();
    }
    finally
    {
        await dbContext.Database.ExecuteSqlRawAsync("RESET app.current_user_id");
        await dbContext.Database.ExecuteSqlRawAsync("RESET app.current_user_ip");
        await dbContext.Database.CloseConnectionAsync();
    }
});

app.MapControllers();
app.Run();
