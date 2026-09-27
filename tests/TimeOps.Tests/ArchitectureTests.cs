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
            var source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "src", "TimeOps." + layer), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains("\\obj\\") && !path.Contains("\\bin\\"))
                .Select(File.ReadAllText));
            Assert.DoesNotContain("using Microsoft.AspNetCore", source);
            Assert.DoesNotContain("using System.Net.Http", source);
            Assert.DoesNotContain("using TimeOps.Infrastructure", source);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TimeOps.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Solução não encontrada.");
    }

    private static string[] References(string root, string layer)
    {
        var file = Path.Combine(root, "src", "TimeOps." + layer, "TimeOps." + layer + ".csproj");
        return XDocument.Load(file).Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value.Replace('\\', '/')))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
}
