using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using TimeOps.Application;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

// Synchronous interop is supported by standalone WebAssembly. Credentials
// never go to the static host; storage is accessed only inside the browser.
public sealed class BrowserConnectionStore(IJSInProcessRuntime javascript) : IConnectionStore
{
    public bool IsAvailable => true;

    public Result<SavedConnection?> Read()
    {
        try
        {
            var json = javascript.Invoke<string?>("timeOpsConnection.read");
            if (json is null) return Result<SavedConnection?>.Success(null);
            var saved = JsonSerializer.Deserialize(json, ConnectionJsonContext.Default.SavedConnection);
            if (saved is null || string.IsNullOrWhiteSpace(saved.Organization) || string.IsNullOrWhiteSpace(saved.PersonalAccessToken))
                return Result<SavedConnection?>.Failure(InvalidConnection());
            return Result<SavedConnection?>.Success(saved);
        }
        catch (JsonException) { return Result<SavedConnection?>.Failure(InvalidConnection()); }
        catch (JSException) { return Result<SavedConnection?>.Failure(StorageError("read")); }
    }

    public Result Save(SavedConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Organization) || string.IsNullOrWhiteSpace(connection.PersonalAccessToken))
            return Result.Failure(InvalidConnection());
        try
        {
            javascript.InvokeVoid("timeOpsConnection.save", JsonSerializer.Serialize(connection, ConnectionJsonContext.Default.SavedConnection));
            return Result.Success();
        }
        catch (JSException) { return Result.Failure(StorageError("save")); }
    }

    public Result Delete()
    {
        try
        {
            javascript.InvokeVoid("timeOpsConnection.remove");
            return Result.Success();
        }
        catch (JSException) { return Result.Failure(StorageError("delete")); }
    }

    private static Error InvalidConnection() => new("connection.store.invalid", ErrorCategory.Unavailable,
        "A conexão salva no navegador está inválida. Remova a credencial salva e conecte novamente.");

    private static Error StorageError(string operation) => new($"connection.store.{operation}", ErrorCategory.Unavailable,
        "Não foi possível acessar a conexão salva. Permita o armazenamento deste site nas configurações do navegador e tente novamente.");
}

[JsonSerializable(typeof(SavedConnection))]
internal partial class ConnectionJsonContext : JsonSerializerContext;
