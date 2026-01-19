using Microsoft.Extensions.FileProviders;

namespace API.Hosting;

public static class EmbeddedStaticFilesExtensions
{
    public static void UseEmbeddedStaticFiles(this WebApplication app)
    {
        var embeddedProvider = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embeddedProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedProvider });

        app.MapFallback(async context =>
        {
            var fileInfo = embeddedProvider.GetFileInfo("index.html");
            if (fileInfo.Exists)
            {
                context.Response.ContentType = "text/html";
                await using var stream = fileInfo.CreateReadStream();
                await stream.CopyToAsync(context.Response.Body);
            }
        });
    }
}
