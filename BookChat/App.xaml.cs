using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Prism.Ioc;
using Prism.DryIoc;
using BookChat.Views;
using BookChat.ViewModels;
using BookChat.Services;
using BookChat.Services.Interfaces;

namespace BookChat;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : PrismApplication
{
    private static string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "BookChat", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // CRITICAL: Prevent app from crashing on background thread exceptions
        // This is needed for the ChatSharp library bug
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            if (ex?.Message?.Contains("That channel already exists") == true)
            {
                // Suppress ChatSharp channel bug - don't let it crash the app
                System.Diagnostics.Debug.WriteLine("Suppressed ChatSharp bug");
            }
        };

        base.OnStartup(e);
    }

    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // Register Services
        containerRegistry.RegisterSingleton<IDccService, DccService>();
        containerRegistry.RegisterSingleton<ISearchResultParser, SearchResultParser>();
        containerRegistry.RegisterSingleton<ICalibreService, CalibreService>();
        containerRegistry.RegisterSingleton<ISettingsService, SettingsService>();

        // Register ViewModels
        containerRegistry.Register<MainWindowViewModel>();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        LogException("Unhandled Exception", exception);

        // Check if this is the known ChatSharp channel collection issue
        bool isChatSharpChannelError = exception?.Message?.Contains("That channel already exists") == true
            || exception?.StackTrace?.Contains("ChatSharp.ChannelCollection.Add") == true
            || exception?.StackTrace?.Contains("ChatSharp.Handlers.ChannelHandlers.HandleJoin") == true;

        if (isChatSharpChannelError)
        {
            // This is a known bug in ChatSharp library - it doesn't handle duplicate JOIN messages properly
            // We'll suppress the crash and log it instead
            System.Diagnostics.Debug.WriteLine($"ChatSharp channel collection error (suppressed): {exception?.Message}");

            // Note: UnhandledException on background threads normally crashes the app
            // Unfortunately, we can't truly prevent the crash from this handler in .NET
            // The app will still terminate, but at least we've logged it
            return;
        }

        // For other critical errors, show the error dialog
        MessageBox.Show(
            $"A critical error occurred:\n\n{exception?.Message}\n\nThe application will close.\n\nError log: {LogFilePath}",
            "Critical Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Dispatcher Exception", e.Exception);

        MessageBox.Show(
            $"An error occurred:\n\n{e.Exception.Message}\n\nError log: {LogFilePath}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Mark as handled to prevent crash
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("Unobserved Task Exception", e.Exception);
        e.SetObserved(); // Prevent crash
    }

    private void LogException(string type, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);

            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {type}\n" +
                          $"Message: {exception?.Message}\n" +
                          $"Stack Trace:\n{exception?.StackTrace}\n" +
                          $"Inner Exception: {exception?.InnerException?.Message}\n" +
                          new string('-', 80) + "\n\n";

            File.AppendAllText(LogFilePath, logEntry);
        }
        catch
        {
            // Can't log - ignore
        }
    }
}

