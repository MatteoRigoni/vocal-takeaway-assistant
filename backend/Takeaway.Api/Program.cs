using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Takeaway.Api.Authorization;
using Takeaway.Api.Data;
using Takeaway.Api.Domain.Constants;
using Takeaway.Api.Domain.Entities;
using Takeaway.Api.Endpoints;
using Takeaway.Api.Hubs;
using Takeaway.Api.Options;
using Takeaway.Api.Services;
using Takeaway.Api.Validation;
using Takeaway.Api.VoiceDialog;
using Takeaway.Api.VoiceDialog.IntentClassification;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=data/takeaway.db";

builder.Services.AddDbContext<TakeawayDbContext>(options =>
    options.UseSqlite(connectionString));

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>();
builder.Services.Configure<OrderThrottlingOptions>(builder.Configuration.GetSection(OrderThrottlingOptions.SectionName));
builder.Services.Configure<OrderCancellationOptions>(builder.Configuration.GetSection(OrderCancellationOptions.SectionName));
builder.Services.Configure<SpeechServicesOptions>(builder.Configuration.GetSection(SpeechServicesOptions.SectionName));
builder.Services.Configure<IntentClassifierOptions>(builder.Configuration.GetSection(IntentClassifierOptions.SectionName));
builder.Services.PostConfigure<SpeechServicesOptions>(options =>
{
    options.SpeechToTextBaseUrl ??= Program.GetUriFromConfiguration(builder.Configuration, "STT:BaseUrl");
    options.TextToSpeechBaseUrl ??= Program.GetUriFromConfiguration(builder.Configuration, "TTS:BaseUrl");
});
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "JWT signing key is required.")
    .ValidateOnStart();
builder.Services.AddOptions<DemoAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(DemoAuthenticationOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.DemoUsers.All(u => u.IsValid()), "Demo users must include username, password hash and role.")
    .ValidateOnStart();
builder.Services.AddScoped<IOrderPricingService, OrderPricingService>();
builder.Services.AddScoped<IOrderThrottlingService, OrderThrottlingService>();
builder.Services.AddScoped<IOrderCodeGenerator, OrderCodeGenerator>();
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddSingleton<IOrderStatusNotifier, OrderStatusNotifier>();
builder.Services.AddSingleton<IKitchenDisplayNotifier, KitchenDisplayNotifier>();
builder.Services.AddSingleton<IOrderCancellationService, OrderCancellationService>();
builder.Services.AddSingleton<IVoiceDialogStateMachine, VoiceDialogStateMachine>();
builder.Services.AddSingleton<IIntentClassifier, MlNetIntentClassifier>();
builder.Services.AddSingleton<IVoiceDialogSessionStore>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<InMemoryVoiceDialogSessionStore>>();
    return new InMemoryVoiceDialogSessionStore(TimeSpan.FromMinutes(30), logger);
});
builder.Services.AddHttpClient<ISpeechToTextClient, FasterWhisperSpeechToTextClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<SpeechServicesOptions>>().Value;
    if (options.SpeechToTextBaseUrl is not null)
    {
        client.BaseAddress = options.SpeechToTextBaseUrl;
    }
});
builder.Services.AddHttpClient<ITextToSpeechClient, PiperTextToSpeechClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<SpeechServicesOptions>>().Value;
    if (options.TextToSpeechBaseUrl is not null)
    {
        client.BaseAddress = options.TextToSpeechBaseUrl;
    }
});
builder.Services.AddSingleton<IDemoUserStore, DemoUserStore>();
builder.Services.AddTakeawayAuthorization();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? throw new InvalidOperationException("JWT options are not configured.");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Run EF Core migrations at startup with logging and simple retry/backoff.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var dbContext = services.GetRequiredService<TakeawayDbContext>();

    const int maxRetries = 3;
    int attempt = 0;
    while (true)
    {
            try
            {
                logger.LogInformation("Applying database migrations (attempt {Attempt}/{Max}).", attempt + 1, maxRetries);
                dbContext.Database.Migrate();
                logger.LogInformation("Database migrations applied successfully.");
                EnsureReferenceData(dbContext, logger);
                break;
            }
        catch (Exception ex)
        {
            attempt++;
            logger.LogWarning(ex, "Database migration failed on attempt {Attempt}/{Max}.", attempt, maxRetries);

            if (attempt >= maxRetries)
            {
                logger.LogError(ex, "Exceeded maximum retry attempts while applying migrations. Stopping application startup.");
                throw;
            }

            // Exponential backoff before retrying (2^attempt seconds, capped)
            var delaySeconds = Math.Min(Math.Pow(2, attempt), 30);
            logger.LogInformation("Waiting {Delay}s before retrying database migration.", delaySeconds);
            Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
        }
    }
}

// Forwarded headers (when behind Caddy/Traefik)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Takeaway API");
        options.WithTheme(ScalarTheme.BluePlanet);
        options.WithSidebar(false);
    });
}

app.MapGet("/", () => Results.Ok(new { app = "voice-ai-takeaway", service = "api", message = "hello from .NET 9" }));
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "api" }));

app.MapAuthEndpoints();
app.MapMenuEndpoints();
app.MapOrdersEndpoints();
app.MapCustomerEndpoints();
app.MapVoiceEndpoints();
app.MapKitchenDisplayEndpoints();
app.MapHub<OrdersHub>("/hubs/orders").RequireAuthorization(AuthorizationPolicies.ViewOrders);
app.MapHub<KdsHub>("/hubs/kds").RequireAuthorization(AuthorizationPolicies.ManageKitchen);

app.Run();

public partial class Program
{
    static void EnsureReferenceData(TakeawayDbContext dbContext, ILogger logger)
    {
        var desiredStatuses = new Dictionary<string, string>
        {
            [OrderStatusCatalog.Received] = "Order received",
            [OrderStatusCatalog.InPreparation] = "Order is being prepared",
            [OrderStatusCatalog.Ready] = "Order ready for pickup",
            [OrderStatusCatalog.Completed] = "Order completed",
            [OrderStatusCatalog.Cancelled] = "Order cancelled"
        };

        var existing = dbContext.OrderStatuses
            .AsNoTracking()
            .ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        var added = false;

        foreach (var (name, description) in desiredStatuses)
        {
            if (existing.ContainsKey(name))
                continue;

            dbContext.OrderStatuses.Add(new OrderStatus
            {
                Name = name,
                Description = description
            });
            logger.LogInformation("Seeding missing order status {Status}", name);
            added = true;
        }

        if (added)
        {
            dbContext.SaveChanges();
        }
    }

    internal static Uri? GetUriFromConfiguration(IConfiguration configuration, string key)
    {
        var value = configuration.GetValue<string>(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }
}
