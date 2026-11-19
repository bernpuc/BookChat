using BookChat.Core;
using BookChat.Core.Services;
using BookChat.Core.Services.Interfaces;
using BookChat.Web.Components;
using BookChat.Web.Data;
using BookChat.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dbPath = builder.Configuration["BookChat:DbPath"] ?? "bookchat.db";
builder.Services.AddDbContextFactory<BookChatDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton<IDccReceiver, DccReceiver>();
builder.Services.AddSingleton<ISearchResultParser, SearchResultParser>();
builder.Services.AddSingleton<ICalibreImportService, CalibreImportService>();

var downloadDirectory = builder.Configuration["BookChat:DownloadDirectory"] ?? "downloads";
builder.Services.AddSingleton(sp => new BookChatSession(
    sp.GetRequiredService<IDccReceiver>(),
    sp.GetRequiredService<ISearchResultParser>(),
    sp.GetRequiredService<ICalibreImportService>(),
    downloadDirectory));

builder.Services.AddHostedService<IrcHostedService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
