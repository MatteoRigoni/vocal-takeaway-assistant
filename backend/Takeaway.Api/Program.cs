using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Takeaway.Api.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=takeaway.db";

builder.Services.AddDbContext<TakeawayDbContext>(options =>
    options.UseSqlite(connectionString));

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new { app = "voice-ai-takeaway", service = "api", message = "hello from .NET 9" }));
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "api" }));

app.Run();
