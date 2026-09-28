using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace TimeOps.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Referencias_respeitam_clean_architecture()
    {
        var root = FindRoot();
        Assert.Empty(References(root, "Domain"));
        Assert.Equal(["TimeOps.Domain"], References(root, "Application"));
        Assert.Equal(["TimeOps.Application", "TimeOps.Domain"], References(root, "Infrastructure"));
        Assert.Equal(["TimeOps.Application", "TimeOps.Infrastructure"], References(root, "Web"));
    }

    [Fact]
    public void Camadas_internas_nao_usam_frameworks_externos()
    {
        var root = FindRoot();
        foreach (var layer in new[] { "Domain", "Application" })
        {
            var sources = Directory.GetFiles(Path.Combine(root, "src", "TimeOps." + layer), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains("\\obj\\") && !path.Contains("\\bin\\"))
                .Select(File.ReadAllText);

            foreach (var sourceFile in sources)
            {
                Assert.DoesNotContain("using Microsoft.AspNetCore", sources);
                Assert.DoesNotContain("using TimeOps.Infrastructure", sources);
            }
        }
    }

    private static string FindRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(sourceFile)!, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TimeOps.slnx")))
                directory = directory.Parent;
            if (directory is not null)
                return directory.FullName;
        }
        throw new InvalidOperationException("Solução não encontrada.");
    }

    private static string[] References(string root, string layer)
    {
        var file = Path.Combine(root, "src", "TimeOps." + layer, "TimeOps." + layer + ".csproj");
        return XDocument.Load(file).Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value.Replace('\\', '/')))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
}
