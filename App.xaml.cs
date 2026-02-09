using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NineLivesAudio.Services;
using NineLivesAudio.ViewModels;
using NineLivesAudio.Views;
using NineLivesAudio.Data;
using NineLivesAudio.Helpers;

namespace NineLivesAudio;

public partial class App : Application
{
    private static IHost? _host;
    private static MiniPlayerWindow? _miniPlayerWindow;

    public static IServiceProvider Services => _host!.Services;

    public App()
    {
        this.InitializeComponent();

        // Migrate legacy "AudioBookshelfApp" folders/vault to "NineLivesAudio"
        LegacyMigrationHelper.MigrateIfNeeded();

        // Build dependency injection container
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Logging (register first so other services can use it)
                services.AddSingleton<ILoggingService, LoggingService>();

                // Services - Core
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IAudioBookshelfApiService, AudioBookshelfApiService>();
                services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
                services.AddSingleton<IDownloadService, DownloadService>();
                services.AddSingleton<ISyncService, SyncService>();
                services.AddSingleton<ILocalDatabase, LocalDatabase>();
                services.AddSingleton<IAppInitializer, AppInitializer>();

                // Services - Connectivity & Offline
                services.AddSingleton<IConnectivityService, ConnectivityService>();
                services.AddSingleton<IOfflineProgressQueue, OfflineProgressQueue>();

                // Services - Navigation
                services.AddSingleton<INavigationService, NavigationService>();

                // Services - UI/UX Enhancements
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<IMetadataNormalizer, MetadataNormalizer>();
                services.AddSingleton<IPlaybackSourceResolver, PlaybackSourceResolver>();

                // ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<HomeViewModel>();
                services.AddTransient<LibraryViewModel>();
                services.AddTransient<PlayerViewModel>();
                services.AddTransient<DownloadsViewModel>();
                services.AddTransient<SettingsViewModel>();

                // Main Window
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Set up global exception handlers
        this.UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // CRITICAL: No blocking async calls here.
        // Show the window IMMEDIATELY, then let MainWindow kick off async init.
        ILoggingService? logger = null;
        try
        {
            logger = Services.GetRequiredService<ILoggingService>();
            logger.Log("App.OnLaunched: activating main window (no async blocking)");

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Activate();

            logger.Log("App.OnLaunched: window activated, async init will run from MainWindow.Loaded");
        }
        catch (Exception ex)
        {
            logger?.LogError("FATAL: App.OnLaunched failed", ex);
            System.Diagnostics.Debug.WriteLine($"FATAL: App launch failed: {ex}");
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logger = Services.GetService<ILoggingService>();
        logger?.LogError("UNHANDLED XAML EXCEPTION", e.Exception);
        logger?.FlushAsync().GetAwaiter().GetResult();
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var logger = Services.GetService<ILoggingService>();
        logger?.LogError("UNHANDLED DOMAIN EXCEPTION", e.ExceptionObject as Exception);
        logger?.FlushAsync().GetAwaiter().GetResult();
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logger = Services.GetService<ILoggingService>();
        logger?.LogError("UNOBSERVED TASK EXCEPTION", e.Exception);
        logger?.FlushAsync().GetAwaiter().GetResult();
        e.SetObserved();
    }

    // --- Mini Player Window Management ---

    public static void OpenMiniPlayer()
    {
        if (_miniPlayerWindow != null)
        {
            _miniPlayerWindow.Activate();
            return;
        }

        _miniPlayerWindow = new MiniPlayerWindow();
        _miniPlayerWindow.Closed += (s, e) => _miniPlayerWindow = null;
        _miniPlayerWindow.Activate();
    }

    public static void CloseMiniPlayer()
    {
        _miniPlayerWindow?.Close();
        _miniPlayerWindow = null;
    }
}
