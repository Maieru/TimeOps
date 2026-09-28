using Microsoft.JSInterop;
using TimeOps.Application;
using TimeOps.Infrastructure;

namespace TimeOps.Tests;

public sealed class BrowserConnectionStoreTests
{
    [Fact]
    public void Salva_restaura_em_nova_instancia_e_remove()
    {
        var browser = new FakeBrowser();
        var store = new BrowserConnectionStore(browser);
        var saved = new SavedConnection("organizacao", "pat-sintetico");
        Assert.Null(store.Read().Value);
        Assert.True(store.Save(saved).IsSuccess);
        Assert.Equal(saved, new BrowserConnectionStore(browser).Read().Value);
        Assert.True(store.Delete().IsSuccess);
        Assert.Null(new BrowserConnectionStore(browser).Read().Value);
    }

    [Fact]
    public void Outro_perfil_nao_recebe_credencial()
    {
        var first = new BrowserConnectionStore(new FakeBrowser());
        first.Save(new("organizacao", "pat-sintetico"));
        Assert.Null(new BrowserConnectionStore(new FakeBrowser()).Read().Value);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Organization\":\"org\",\"PersonalAccessToken\":\"\"}")]
    public void Credencial_corrompida_pode_ser_removida(string json)
    {
        var store = new BrowserConnectionStore(new FakeBrowser { Stored = json });
        Assert.Equal("connection.store.invalid", store.Read().Error!.Code);
        Assert.True(store.Delete().IsSuccess);
        Assert.Null(store.Read().Value);
    }

    [Fact]
    public void Falhas_do_browser_nao_expoem_token()
    {
        var store = new BrowserConnectionStore(new FakeBrowser { Fail = true });
        Assert.DoesNotContain("pat-sintetico", store.Read().Error!.Message);
        Assert.DoesNotContain("pat-sintetico", store.Save(new("org", "pat-sintetico")).Error!.Message);
        Assert.DoesNotContain("pat-sintetico", store.Delete().Error!.Message);
    }

    private sealed class FakeBrowser : IJSInProcessRuntime
    {
        public string? Stored { get; set; }
        public bool Fail { get; init; }
        public TValue Invoke<TValue>(string identifier, params object?[]? args)
        {
            if (Fail) throw new JSException("pat-sintetico must not appear in errors");
            switch (identifier)
            {
                case "timeOpsConnection.read": return (TValue)(object?)Stored!;
                case "timeOpsConnection.save": Stored = (string)args![0]!; break;
                case "timeOpsConnection.remove": Stored = null; break;
                default: throw new InvalidOperationException(identifier);
            }
            return default!;
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
    }
}
