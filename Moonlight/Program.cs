using BlazorDownloadFile;
using BlazorTable;
using CurrieTechnologies.Razor.SweetAlert2;
using HealthChecks.UI.Client;
using Logging.Net;
using Moonlight.App.ApiClients.CloudPanel;
using Moonlight.App.ApiClients.Daemon;
using Moonlight.App.ApiClients.Modrinth;
using Moonlight.App.ApiClients.Paper;
using Moonlight.App.ApiClients.Wings;
using Moonlight.App.Database;
using Moonlight.App.Diagnostics.HealthChecks;
using Moonlight.App.Events;
using Moonlight.App.Helpers;
using Moonlight.App.Helpers.Wings;
using Moonlight.App.LogMigrator;
using Moonlight.App.Repositories;
using Moonlight.App.Repositories.Domains;
using Moonlight.App.Repositories.LogEntries;
using Moonlight.App.Repositories.Servers;
using Moonlight.App.Services;
using Moonlight.App.Services.Addon;
using Moonlight.App.Services.Background;
using Moonlight.App.Services.DiscordBot;
using Moonlight.App.Services.Files;
using Moonlight.App.Services.Interop;
using Moonlight.App.Services.LogServices;
using Moonlight.App.Services.Mail;
using Moonlight.App.Services.Minecraft;
using Moonlight.App.Services.Notifications;
using Moonlight.App.Services.Sessions;
using Moonlight.App.Services.Statistics;
using Moonlight.App.Services.SupportChat;
using Moonlight.App.Services.Tickets;

namespace Moonlight
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Logger.UsedLogger = new CacheLogger();

            Logger.Info($"Working dir: {Directory.GetCurrentDirectory()}");

            Logger.Info("Running pre-init tasks");
            
            // This will also copy all default config files
            var configService = new ConfigService(new StorageService());
            var databaseCheckupService = new DatabaseCheckupService(configService);
                
            await databaseCheckupService.Perform();
            
            var builder = WebApplication.CreateBuilder(args);

            // Switch to logging.net injection
            // TODO: Enable in production
            //builder.Logging.ClearProviders();
            //builder.Logging.AddProvider(new LogMigratorProvider());

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddServerSideBlazor()
                .AddHubOptions(options =>
                {
                    options.MaximumReceiveMessageSize = 10000000;
                    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
                    options.HandshakeTimeout = TimeSpan.FromSeconds(10);
                });
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddHealthChecks()
                .AddCheck<DatabaseHealthCheck>("Database")
                .AddCheck<NodeHealthCheck>("Nodes")
                .AddCheck<DaemonHealthCheck>("Daemons");

            // Databases
            builder.Services.AddDbContext<DataContext>();

            // Repositories
            builder.Services.AddSingleton<SessionRepository>();
            builder.Services.AddScoped<UserRepository>();
            builder.Services.AddScoped<NodeRepository>();
            builder.Services.AddScoped<ServerRepository>();
            builder.Services.AddScoped<ServerBackupRepository>();
            builder.Services.AddScoped<ImageRepository>();
            builder.Services.AddScoped<DomainRepository>();
            builder.Services.AddScoped<SharedDomainRepository>();
            builder.Services.AddScoped<RevokeRepository>();
            builder.Services.AddScoped<NotificationRepository>();
            builder.Services.AddScoped<DdosAttackRepository>();
            builder.Services.AddScoped<SubscriptionRepository>();
            builder.Services.AddScoped<LoadingMessageRepository>();
            builder.Services.AddScoped<NewsEntryRepository>();
            builder.Services.AddScoped<NodeAllocationRepository>();
            builder.Services.AddScoped<StatisticsRepository>();
            builder.Services.AddScoped<AuditLogEntryRepository>();
            builder.Services.AddScoped<ErrorLogEntryRepository>();
            builder.Services.AddScoped<SecurityLogEntryRepository>();
            builder.Services.AddScoped(typeof(Repository<>));

            // Services
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddSingleton<StorageService>();
            builder.Services.AddScoped<CookieService>();
            builder.Services.AddScoped<IdentityService>();
            builder.Services.AddScoped<IpLocateService>();
            builder.Services.AddScoped<SessionService>();
            builder.Services.AddScoped<AlertService>();
            builder.Services.AddScoped<SmartTranslateService>();
            builder.Services.AddScoped<UserService>();
            builder.Services.AddScoped<TotpService>();
            builder.Services.AddScoped<ToastService>();
            builder.Services.AddScoped<NodeService>();
            builder.Services.AddScoped<ServerService>();
            builder.Services.AddSingleton<PaperService>();
            builder.Services.AddScoped<ClipboardService>();
            builder.Services.AddSingleton<ResourceService>();
            builder.Services.AddScoped<DomainService>();
            builder.Services.AddScoped<OneTimeJwtService>();
            builder.Services.AddSingleton<NotificationServerService>();
            builder.Services.AddScoped<NotificationAdminService>();
            builder.Services.AddScoped<ModalService>();
            builder.Services.AddScoped<SmartDeployService>();
            builder.Services.AddScoped<WebSpaceService>();
            builder.Services.AddScoped<StatisticsViewService>();
            builder.Services.AddSingleton<DateTimeService>();
            builder.Services.AddSingleton<EventSystem>();
            builder.Services.AddScoped<FileDownloadService>();
            builder.Services.AddScoped<ForgeService>();
            builder.Services.AddScoped<FabricService>();
            builder.Services.AddSingleton<BucketService>();
            builder.Services.AddScoped<RatingService>();
            builder.Services.AddScoped<ReCaptchaService>();
            builder.Services.AddScoped<IpBanService>();
            builder.Services.AddSingleton<OAuth2Service>();
            builder.Services.AddScoped<DynamicBackgroundService>();
            builder.Services.AddScoped<ServerAddonPluginService>();

            builder.Services.AddScoped<SubscriptionService>();
            builder.Services.AddScoped<SubscriptionAdminService>();

            // Loggers
            builder.Services.AddScoped<SecurityLogService>();
            builder.Services.AddScoped<AuditLogService>();
            builder.Services.AddScoped<ErrorLogService>();
            builder.Services.AddScoped<LogService>();
            builder.Services.AddScoped<MailService>();
            builder.Services.AddSingleton<TrashMailDetectorService>();

            // Support chat
            builder.Services.AddSingleton<SupportChatServerService>();
            builder.Services.AddScoped<SupportChatClientService>();
            builder.Services.AddScoped<SupportChatAdminService>();
            
            //Ticket System
            builder.Services.AddSingleton<TicketServerService>();
            builder.Services.AddScoped<TicketClientService>();

            // Helpers
            builder.Services.AddSingleton<SmartTranslateHelper>();
            builder.Services.AddScoped<WingsApiHelper>();
            builder.Services.AddScoped<WingsServerConverter>();
            builder.Services.AddSingleton<WingsJwtHelper>();
            builder.Services.AddScoped<WingsConsoleHelper>();
            builder.Services.AddSingleton<PaperApiHelper>();
            builder.Services.AddSingleton<HostSystemHelper>();
            builder.Services.AddScoped<DaemonApiHelper>();
            builder.Services.AddScoped<CloudPanelApiHelper>();
            builder.Services.AddScoped<ModrinthApiHelper>();

            // Background services
            builder.Services.AddSingleton<DiscordBotService>();
            builder.Services.AddSingleton<StatisticsCaptureService>();
            builder.Services.AddSingleton<DiscordNotificationService>();
            builder.Services.AddSingleton<CleanupService>();
            
            // Other
            builder.Services.AddSingleton<MoonlightService>();

            // Third party services
            builder.Services.AddBlazorTable();
            builder.Services.AddSweetAlert2(options => { options.Theme = SweetAlertTheme.Dark; });
            builder.Services.AddBlazorContextMenu();
            builder.Services.AddBlazorDownloadFile();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStaticFiles();
            app.UseRouting();
            app.UseWebSockets();

            app.MapControllers();

            app.MapBlazorHub();
            app.MapFallbackToPage("/_Host");
            app.MapHealthChecks("/_health", new()
            {
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });

            // AutoStart services
            _ = app.Services.GetRequiredService<CleanupService>();
            _ = app.Services.GetRequiredService<DiscordBotService>();
            _ = app.Services.GetRequiredService<StatisticsCaptureService>();
            _ = app.Services.GetRequiredService<DiscordNotificationService>();
            
            _ = app.Services.GetRequiredService<MoonlightService>();

            // Discord bot service
            //var discordBotService = app.Services.GetRequiredService<DiscordBotService>();

            await app.RunAsync();
        }
    }
}