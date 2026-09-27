using TimeOps.Application;
using TimeOps.Infrastructure;

namespace TimeOps.Tests;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void Credencial_de_teste_sobrevive_a_nova_instancia_e_pode_ser_removida()
    {
        if (!OperatingSystem.IsWindows()) return;

        var target = $"TimeOps.Tests.{Guid.NewGuid():N}";
        var first = new WindowsCredentialStore(target);
        try
        {
            Assert.True(first.Save(new SavedConnection("org-a", "pat-sintetico-a")).IsSuccess);
            var second = new WindowsCredentialStore(target);
            Assert.Equal(new SavedConnection("org-a", "pat-sintetico-a"), second.Read().Value);

            Assert.True(second.Save(new SavedConnection("org-b", "pat-sintetico-b")).IsSuccess);
            Assert.Equal(new SavedConnection("org-b", "pat-sintetico-b"), first.Read().Value);
        }
        finally
        {
            Assert.True(first.Delete().IsSuccess);
        }
        Assert.Null(new WindowsCredentialStore(target).Read().Value);
    }
}
