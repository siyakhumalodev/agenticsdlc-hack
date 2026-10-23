using OctoSupply.Api.Data;
using OctoSupply.Api.Repositories;
using OctoSupply.Api.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using OctoSupply.Api.Controllers;
using OctoSupply.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Framework request/action logs can contain bearer cart identifiers in API paths.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    var defaultFactory = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
        context.ActionDescriptor is ControllerActionDescriptor descriptor &&
        descriptor.ControllerTypeInfo.AsType() == typeof(CartsController)
            ? new BadRequestObjectResult(new CartErrorResponse(
                new CartError("VALIDATION_ERROR", "Validation error: provide valid identifiers and positive integer quantities")))
            : defaultFactory(context);
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddScoped<BranchesRepository>();
builder.Services.AddScoped<DeliveriesRepository>();
builder.Services.AddScoped<HeadquartersRepository>();
builder.Services.AddScoped<OrderDetailDeliveriesRepository>();
builder.Services.AddScoped<OrderDetailsRepository>();
builder.Services.AddScoped<OrdersRepository>();
builder.Services.AddScoped<ProductsRepository>();
builder.Services.AddScoped<CartsRepository>();
builder.Services.AddScoped<SuppliersRepository>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("ApiCors", policy =>
    {
        var configuredOrigins = Environment.GetEnvironmentVariable("API_CORS_ORIGINS");
        if (!string.IsNullOrWhiteSpace(configuredOrigins))
        {
            var origins = configuredOrigins
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            return;
        }

        policy
            .SetIsOriginAllowed(origin =>
                origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) ||
                origin.Contains(".app.github.dev", StringComparison.OrdinalIgnoreCase) ||
                origin.Contains(".azurecontainerapps.io", StringComparison.OrdinalIgnoreCase))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.MapOpenApi("/api-docs.json");
app.UseCors("ApiCors");
app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapGet("/", () => "Hello, world!");
app.MapControllers();

var migrator = app.Services.GetRequiredService<DatabaseMigrator>();
await migrator.InitializeAsync(seedOnStartup: true);

app.Run();

/// <summary>Exposes the application entry point to HTTP integration tests.</summary>
public partial class Program;
