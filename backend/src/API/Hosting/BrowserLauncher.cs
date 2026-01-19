using System.Diagnostics;
using System.Runtime.InteropServices;

namespace API.Hosting;

public static class BrowserLauncher
{
    public static void Open(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var startInfo = new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };
                Process.Start(startInfo);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open browser: {ex.Message}");
        }
    }

    public static void OpenWithRetry(string url, int maxRetries = 10, int delayMs = 300)
    {
        Task.Run(async () =>
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

            for (int i = 0; i < maxRetries; i++)
            {
                await Task.Delay(delayMs);

                try
                {
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        Open(url);
                        Console.WriteLine($"Browser opened successfully (attempt {i + 1})");
                        return;
                    }
                }
                catch
                {
                    Console.WriteLine($"Waiting for server... (attempt {i + 1}/{maxRetries})");
                }
            }

            Console.WriteLine("Server check timed out, opening browser anyway...");
            Open(url);
        });
    }
}
