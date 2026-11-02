using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Cinedex.Core.Localization;

namespace Cinedex.App.Security;

/// <summary>Coffre à secrets du système : jamais de secret en clair dans les fichiers de Cinedex.</summary>
public interface ISecretStore
{
    string? Read(string name);

    void Write(string name, string secret);

    void Delete(string name);
}

public static class SecretStore
{
    public const string TmdbToken = "tmdb-read-token";
    public const string OmdbKey = "omdb-api-key";

    /// <summary>Nom de l'application avant son renommage : les secrets enregistrés sous ce nom sont repris.</summary>
    public const string LegacyName = "Bobine";

    public static ISecretStore ForCurrentPlatform() => new MigratingSecretStore(ForName("Cinedex"), ForName(LegacyName));

    private static ISecretStore ForName(string appName)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore(appName);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainStore(appName);
        }

        return new LinuxSecretToolStore(appName.ToLowerInvariant());
    }
}

/// <summary>
/// Coffre actuel, avec reprise de l'ancien nom de l'application : un secret trouvé seulement sous l'ancien nom
/// est réécrit sous le nouveau puis effacé de l'ancien, à la première lecture.
/// </summary>
public sealed class MigratingSecretStore(ISecretStore current, ISecretStore legacy) : ISecretStore
{
    public string? Read(string name)
    {
        if (current.Read(name) is { } value)
        {
            return value;
        }

        if (legacy.Read(name) is not { } old)
        {
            return null;
        }

        current.Write(name, old);
        legacy.Delete(name);
        return old;
    }

    public void Write(string name, string secret) => current.Write(name, secret);

    public void Delete(string name)
    {
        current.Delete(name);
        legacy.Delete(name);
    }
}

/// <summary>Gestionnaire d'identifiants Windows (chiffré par DPAPI, lié au compte utilisateur).</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsCredentialStore(string appName) : ISecretStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? Read(string name)
    {
        if (!CredRead(Target(name), CredTypeGeneric, 0, out var handle))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw new InvalidOperationException(Tr.F("Lecture du coffre impossible (erreur {0}).", error));
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            if (credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(handle);
        }
    }

    public void Write(string name, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        var target = Marshal.StringToHGlobalUni(Target(name));
        var user = Marshal.StringToHGlobalUni(Environment.UserName);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = user,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException(Tr.F("Écriture dans le coffre impossible (erreur {0}).", Marshal.GetLastPInvokeError()));
            }
        }
        finally
        {
            // Efface la copie non managée du secret avant de libérer la mémoire.
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(user);
            Array.Clear(bytes);
        }
    }

    public void Delete(string name)
    {
        if (!CredDelete(Target(name), CredTypeGeneric, 0) && Marshal.GetLastPInvokeError() != ErrorNotFound)
        {
            throw new InvalidOperationException("Suppression dans le coffre impossible.");
        }
    }

    private string Target(string name) => appName + "/" + name;

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, int type, int flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref Credential credential, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, int type, int flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}

/// <summary>Trousseau macOS via l'outil système <c>security</c>.</summary>
internal sealed class MacKeychainStore(string service) : ISecretStore
{
    public string? Read(string name)
    {
        var (code, output) = Tool.Run("security", ["find-generic-password", "-s", service, "-a", name, "-w"]);
        return code == 0 ? output.TrimEnd('\n') : null;
    }

    // Limite connue : « security » ne lit pas le secret sur l'entrée standard ; il transite brièvement en argument.
    public void Write(string name, string secret) =>
        Tool.Check(Tool.Run("security", ["add-generic-password", "-U", "-s", service, "-a", name, "-w", secret]));

    public void Delete(string name) => Tool.Run("security", ["delete-generic-password", "-s", service, "-a", name]);
}

/// <summary>Service Secret de freedesktop (GNOME Keyring, KWallet) via <c>secret-tool</c>.</summary>
internal sealed class LinuxSecretToolStore(string service) : ISecretStore
{
    public string? Read(string name)
    {
        var (code, output) = Tool.Run("secret-tool", ["lookup", "service", service, "account", name]);
        return code == 0 && output.Length > 0 ? output.TrimEnd('\n') : null;
    }

    public void Write(string name, string secret) =>
        Tool.Check(Tool.Run("secret-tool", ["store", "--label=" + service, "service", service, "account", name], stdin: secret));

    public void Delete(string name) => Tool.Run("secret-tool", ["clear", "service", service, "account", name]);
}

internal static class Tool
{
    /// <summary>Lance un outil système sans shell, arguments passés un par un.</summary>
    public static (int Code, string Output) Run(string file, IReadOnlyList<string> args, string? stdin = null)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException(Tr.F("{0} n'a pas démarré.", file));
            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return (process.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(Tr.F("Coffre à secrets indisponible : l'outil « {0} » est introuvable.", file), ex);
        }
    }

    public static void Check((int Code, string Output) result)
    {
        if (result.Code != 0)
        {
            throw new InvalidOperationException(Tr.F("Le coffre à secrets a refusé l'opération (code {0}).", result.Code));
        }
    }
}
