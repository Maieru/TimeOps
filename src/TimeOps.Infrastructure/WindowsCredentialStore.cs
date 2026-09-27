using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using TimeOps.Application;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

// Generic credentials are encrypted by Windows for the account running TimeOps.
public sealed class WindowsCredentialStore(string targetName = "TimeOps.AzureDevOps.Default") : IConnectionStore
{
    private const uint GenericCredential = 1;
    private const uint LocalMachinePersistence = 2;
    private const int NotFound = 1168;
    private const int MaxBlobBytes = 2560;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public Result<SavedConnection?> Read()
    {
        if (!IsAvailable) return Result<SavedConnection?>.Success(null);
        if (!CredRead(targetName, GenericCredential, 0, out var pointer))
        {
            var code = Marshal.GetLastWin32Error();
            return code == NotFound
                ? Result<SavedConnection?>.Success(null)
                : Result<SavedConnection?>.Failure(StoreError("connection.store.read", "Não foi possível ler a conexão salva no Windows.", code));
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var organization = Marshal.PtrToStringUni(credential.UserName);
            if (string.IsNullOrWhiteSpace(organization) || credential.CredentialBlobSize is 0 or > MaxBlobBytes || credential.CredentialBlob == IntPtr.Zero)
                return Result<SavedConnection?>.Failure(new("connection.store.invalid", ErrorCategory.Unavailable, "A conexão salva está incompleta. Remova-a e informe o PAT novamente."));

            var bytes = new byte[(int)credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Result<SavedConnection?>.Success(new SavedConnection(organization, Encoding.UTF8.GetString(bytes)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Result Save(SavedConnection connection)
    {
        if (!IsAvailable)
            return Result.Failure(new("connection.store.unsupported", ErrorCategory.Unavailable, "O armazenamento seguro da conexão está disponível apenas no Windows."));
        if (string.IsNullOrWhiteSpace(connection.Organization) || string.IsNullOrWhiteSpace(connection.PersonalAccessToken))
            return Result.Failure(new("connection.store.invalid", ErrorCategory.Validation, "Informe a organização e o PAT antes de salvar a conexão."));
        if (connection.Organization.Length > 513)
            return Result.Failure(new("connection.store.organization", ErrorCategory.Validation, "O nome da organização é longo demais para o Gerenciador de Credenciais."));

        var bytes = Encoding.UTF8.GetBytes(connection.PersonalAccessToken);
        if (bytes.Length > MaxBlobBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            return Result.Failure(new("connection.store.token", ErrorCategory.Validation, "O PAT é longo demais para o Gerenciador de Credenciais."));
        }

        var targetPointer = IntPtr.Zero;
        var organizationPointer = IntPtr.Zero;
        var blobPointer = IntPtr.Zero;
        try
        {
            targetPointer = Marshal.StringToHGlobalUni(targetName);
            organizationPointer = Marshal.StringToHGlobalUni(connection.Organization);
            blobPointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, blobPointer, bytes.Length);

            var native = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = targetPointer,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blobPointer,
                Persist = LocalMachinePersistence,
                UserName = organizationPointer
            };
            return CredWrite(ref native, 0)
                ? Result.Success()
                : Result.Failure(StoreError("connection.store.write", "Não foi possível salvar a conexão no Windows.", Marshal.GetLastWin32Error()));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (blobPointer != IntPtr.Zero)
            {
                Marshal.Copy(new byte[bytes.Length], 0, blobPointer, bytes.Length);
                Marshal.FreeHGlobal(blobPointer);
            }
            if (organizationPointer != IntPtr.Zero) Marshal.FreeHGlobal(organizationPointer);
            if (targetPointer != IntPtr.Zero) Marshal.FreeHGlobal(targetPointer);
        }
    }

    public Result Delete()
    {
        if (!IsAvailable) return Result.Success();
        if (CredDelete(targetName, GenericCredential, 0)) return Result.Success();
        var code = Marshal.GetLastWin32Error();
        return code == NotFound
            ? Result.Success()
            : Result.Failure(StoreError("connection.store.delete", "Não foi possível remover a conexão salva do Windows.", code));
    }

    private static Error StoreError(string code, string message, int win32Code)
        => new(code, ErrorCategory.Unavailable, $"{message} (Windows: {win32Code}).");

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);
}
