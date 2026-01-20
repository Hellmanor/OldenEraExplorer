using NotificationIcon.NET;

namespace API.Hosting;

public static class TrayIconExtensions
{
    public static void UseTrayIcon(this WebApplication app)
    {
        NotifyIcon? trayIcon = null;
        string? tempIconPath = null;

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var url = "http://localhost:5176";
            Console.WriteLine($"Server started at {url}");

            Task.Run(() =>
            {
                try
                {
                    (trayIcon, tempIconPath) = CreateTrayIcon(url, () => app.StopAsync());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Tray icon failed: {ex.Message}");
                }
            });

            BrowserLauncher.OpenWithRetry(url);
        });

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            trayIcon?.Dispose();
            if (tempIconPath != null && File.Exists(tempIconPath))
            {
                try { File.Delete(tempIconPath); } catch { }
            }
        });
    }

    private static (NotifyIcon? Icon, string? TempPath) CreateTrayIcon(string url, Func<Task> shutdownAction)
    {
        try
        {
            // Windows supports .ico format (multi-resolution), Linux/macOS work better with PNG
            var iconResourceName = OperatingSystem.IsWindows() ? "favicon.ico" : "tray-icon.png";
            var assembly = typeof(TrayIconExtensions).Assembly;
            using var stream = assembly.GetManifestResourceStream(iconResourceName);

            if (stream == null)
            {
                Console.WriteLine($"Tray icon: embedded resource '{iconResourceName}' not found");
                return (null, null);
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"olden-era-explorer-{iconResourceName}");
            using (var fileStream = File.Create(tempPath))
            {
                stream.CopyTo(fileStream);
            }

            var menuItems = new List<MenuItem>
            {
                new("Olden Era Explorer") { IsDisabled = true },
                new("-"),
                new("Open Browser") { Click = (_, _) => BrowserLauncher.Open(url) },
                new("-"),
                new("Exit") { Click = (_, _) => shutdownAction() }
            };

            var icon = NotifyIcon.Create(tempPath, menuItems);
            Console.WriteLine("System tray icon active. Right-click to access menu.");
            icon.Show();
            return (icon, tempPath);
        }
        catch (PlatformNotSupportedException)
        {
            Console.WriteLine("System tray icon not available on this platform.");
            Console.WriteLine("To exit: press Ctrl+C or close this terminal.");
            return (null, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Tray icon failed: {ex.Message}");
            return (null, null);
        }
    }
}
