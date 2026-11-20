using Microsoft.Win32;
using Serilog;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace HakamiqChdTool.App.Services.Shell;

/// <summary>Display text for the Explorer menu entries, in the current UI language.</summary>
internal sealed record ExplorerMenuText(string Convert, string Extract, string Verify, string FolderMenu);

/// <summary>
/// Adds or removes the CHD Tool entries in the Explorer right-click menu for the current Windows account
/// only (HKCU\Software\Classes, no administrator rights). Disc images and archives get "Convert to CHD";
/// CHD files get Extract and Verify; folders get a CHD Tool submenu with all three. Each entry starts the
/// app with <c>--mode &lt;mode&gt; --add "%1"</c>. Only keys whose name starts with <see cref="VerbPrefix"/>
/// are written or deleted.
/// </summary>
internal sealed class ExplorerMenuRegistration
{
    public const string VerbPrefix = "HakamiqChdTool";
    private const string FolderMenuKey = VerbPrefix + ".Menu";

    /// <summary>Inputs that convert from a single selected file. BIN, IMG and HDD are left out: a BIN needs its
    /// CUE, and IMG/HDD are too generic to put an entry on every such file.</summary>
    public static IReadOnlyList<string> ConvertExtensions { get; } =
    [
        ".iso", ".cso", ".zso", ".cue", ".gdi", ".toc", ".nrg", ".ccd", ".mds", ".ecm", ".zip", ".7z", ".rar"
    ];

    public static IReadOnlyList<string> ChdExtensions { get; } = [".chd"];

    private readonly RegistryKey _root;
    private readonly string _classesPath;

    /// <param name="root">The hive; production uses <see cref="Registry.CurrentUser"/>.</param>
    /// <param name="classesPath">Path of the Classes key under <paramref name="root"/>; tests pass a private key.</param>
    public ExplorerMenuRegistration(RegistryKey root, string classesPath = @"Software\Classes")
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(classesPath);
        _root = root;
        _classesPath = classesPath;
    }

    public static ExplorerMenuRegistration ForCurrentUser() => new(Registry.CurrentUser);

    /// <summary>Writes every entry for <paramref name="exePath"/>; safe to repeat (updates path and text).</summary>
    public bool TryRegister(string exePath, ExplorerMenuText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(exePath) || !Path.IsPathFullyQualified(exePath) || exePath.Contains('"', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using RegistryKey classes = _root.CreateSubKey(_classesPath, writable: true);
            foreach (string extension in ConvertExtensions)
            {
                WriteVerb(classes, $@"SystemFileAssociations\{extension}\shell\{VerbPrefix}.Convert", text.Convert, exePath, "convert");
            }

            foreach (string extension in ChdExtensions)
            {
                WriteVerb(classes, $@"SystemFileAssociations\{extension}\shell\{VerbPrefix}.Extract", text.Extract, exePath, "extract");
                WriteVerb(classes, $@"SystemFileAssociations\{extension}\shell\{VerbPrefix}.Verify", text.Verify, exePath, "verify");
            }

            using (RegistryKey menu = classes.CreateSubKey($@"Directory\shell\{FolderMenuKey}", writable: true))
            {
                menu.SetValue("MUIVerb", text.FolderMenu);
                menu.SetValue("Icon", $"\"{exePath}\",0");
                menu.SetValue("SubCommands", string.Empty);
            }

            WriteVerb(classes, $@"Directory\shell\{FolderMenuKey}\shell\1.Convert", text.Convert, exePath, "convert");
            WriteVerb(classes, $@"Directory\shell\{FolderMenuKey}\shell\2.Extract", text.Extract, exePath, "extract");
            WriteVerb(classes, $@"Directory\shell\{FolderMenuKey}\shell\3.Verify", text.Verify, exePath, "verify");

            NotifyShell();
            return true;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            Log.Warning(ex, "Explorer menu registration failed.");
            return false;
        }
    }

    /// <summary>Removes every CHD Tool entry, including ones for extensions no longer in the list.</summary>
    public bool TryUnregister()
    {
        try
        {
            using RegistryKey? classes = _root.OpenSubKey(_classesPath, writable: true);
            if (classes is null)
            {
                return true;
            }

            using (RegistryKey? associations = classes.OpenSubKey("SystemFileAssociations", writable: false))
            {
                foreach (string extension in associations?.GetSubKeyNames() ?? [])
                {
                    DeleteOwnVerbs(classes, $@"SystemFileAssociations\{extension}\shell");
                }
            }

            DeleteOwnVerbs(classes, @"Directory\shell");
            NotifyShell();
            return true;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            Log.Warning(ex, "Explorer menu removal failed.");
            return false;
        }
    }

    public bool IsRegistered()
    {
        try
        {
            using RegistryKey? key = _root.OpenSubKey($@"{_classesPath}\Directory\shell\{FolderMenuKey}", writable: false);
            return key is not null;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return false;
        }
    }

    internal static string BuildCommand(string exePath, string mode) => $"\"{exePath}\" --mode {mode} --add \"%1\"";

    private static void WriteVerb(RegistryKey classes, string verbPath, string text, string exePath, string mode)
    {
        using RegistryKey verb = classes.CreateSubKey(verbPath, writable: true);
        verb.SetValue("MUIVerb", text);
        verb.SetValue("Icon", $"\"{exePath}\",0");
        verb.SetValue("MultiSelectModel", "Player");
        using RegistryKey command = verb.CreateSubKey("command", writable: true);
        command.SetValue(string.Empty, BuildCommand(exePath, mode));
    }

    private static void DeleteOwnVerbs(RegistryKey classes, string shellPath)
    {
        using RegistryKey? shell = classes.OpenSubKey(shellPath, writable: true);
        if (shell is null)
        {
            return;
        }

        foreach (string name in shell.GetSubKeyNames())
        {
            if (name.StartsWith(VerbPrefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                shell.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
        }
    }

    private static bool IsRegistryFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or ObjectDisposedException;

    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Debug(ex, "Shell change notification is unavailable.");
        }
    }

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
