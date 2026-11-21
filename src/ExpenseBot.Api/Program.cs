using System.Diagnostics;
using ExpenseBot.Api.Telegram;
using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Categories;
using ExpenseBot.Application.Formatting;
using ExpenseBot.Application.Options;
using ExpenseBot.Application.Summaries;
using ExpenseBot.Application.Transactions;
using ExpenseBot.Application.Updates;
using ExpenseBot.Application.Users;
using ExpenseBot.Infrastructure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

// Application
builder.Services.AddSingleton<CategoryCache>();
builder.Services.AddSingleton<ICategoryProvider>(sp => sp.GetRequiredService<CategoryCache>());
builder.Services.AddSingleton<TransactionFormatter>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<CommandHandler>();
builder.Services.AddScoped<UpdateHandler>();

// Infrastructure (Mongo + Telegram client)
builder.Services.AddInfrastructure(builder.Configuration);

// Presentation
builder.Services.AddHostedService<PollingWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(); // serves /swagger/index.html

    // Open Swagger UI in the default browser using the address the app is actually bound to.
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses ?? [];

        var address = addresses.FirstOrDefault(a => a.StartsWith("https://"))
                        ?? addresses.FirstOrDefault();
        if (address is null) return;

        // Wildcard bindings (0.0.0.0, [::], *, +) aren't browsable; use localhost instead.
        var uri = new UriBuilder(address.Replace("://*", "://localhost").Replace("://+", "://localhost"));
        if (uri.Host is "0.0.0.0" or "[::]" or "::") uri.Host = "localhost";
        uri.Path = "swagger/index.html";

        var url = uri.Uri.ToString();
        app.Logger.LogInformation("Opening Swagger UI: {Url}", url);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Could not open browser for {Url}", url);
        }
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
