using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace TimeOps.E2E.Host;

// Test-only Kestrel host: the application itself still runs in WebAssembly.
public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        var root = app.Configuration["StaticFilesPath"]
            ?? throw new InvalidOperationException("Configure StaticFilesPath with the published wwwroot directory.");
        using var files = new PhysicalFileProvider(root);
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".wasm"] = "application/wasm";
        types.Mappings[".dat"] = "application/octet-stream";
        var options = new StaticFileOptions { FileProvider = files, ContentTypeProvider = types };
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(options);
        app.MapFallbackToFile("index.html", options);
        app.Run();
    }
}
