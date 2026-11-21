using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CharTweak3;

/// <summary>OS file selection on a worker, never a shell command or a blocking game/UI call.</summary>
internal static class PortraitPicker
{
    private static int s_open;
    internal static bool IsOpen => Volatile.Read(ref s_open) != 0;

    internal static Task<string?> Choose() => Start("CharTweak 3 — Choose portrait");
    internal static Task<string?> ChooseBackground() => Start("CharTweak 3 — Choose photo background");
    private static Task<string?> Start(string title)
    {
        if (Interlocked.CompareExchange(ref s_open, 1, 0) != 0)
            return Task.FromException<string?>(new InvalidOperationException("A portrait file chooser is already open."));
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            string? result = null;
            Exception? failure = null;
            try
            {
                result = OperatingSystem.IsWindows() ? WindowsChoose(title)
                    : OperatingSystem.IsLinux() ? LinuxChoose(title) : throw new PlatformNotSupportedException("Portrait selection supports Linux and Windows.");
            }
            catch (Exception e) { failure = e; }
            finally { Volatile.Write(ref s_open, 0); }
            if (failure is null) completion.SetResult(result);
            else completion.SetException(failure);
        }) { IsBackground = true, Name = "CharTweak portrait chooser" };
        if (OperatingSystem.IsWindows()) worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    private static string? LinuxChoose(string title)
    {
        bool kde = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.Contains("KDE", StringComparison.OrdinalIgnoreCase) == true;
        foreach (string tool in kde ? new[] { "kdialog", "zenity" } : new[] { "zenity", "kdialog" })
        {
            var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            string[] arguments = tool == "zenity"
                ? ["--file-selection", "--title=" + title, "--file-filter=Images | *.png *.jpg *.jpeg *.bmp *.tga *.PNG *.JPG *.JPEG *.BMP *.TGA", "--file-filter=All files | *"]
                : ["--title", title, "--getopenfilename", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "*.png *.jpg *.jpeg *.bmp *.tga *.PNG *.JPG *.JPEG *.BMP *.TGA|Images"];
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            try
            {
                using var process = Process.Start(start) ?? throw new IOException("File chooser could not start.");
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                process.WaitForExit(); Task.WaitAll(output, error);
                if (process.ExitCode == 1) return null; // Native Cancel, never open a second chooser after cancellation.
                if (process.ExitCode != 0) continue;
                string path = output.Result.TrimEnd('\r', '\n');
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch (Win32Exception) { /* Try the other desktop's chooser when this one is not installed. */ }
        }
        throw new IOException("Install zenity or kdialog to choose portrait images on Linux.");
    }

    // OPENFILENAMEW's classic full structure, including its reserved extension fields.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int Size;
        public nint Owner, Instance;
        public string Filter;
        public nint CustomFilter;
        public int MaximumCustomFilter, FilterIndex;
        public nint File;
        public int MaximumFile;
        public nint FileTitle;
        public int MaximumFileTitle;
        public string? InitialDirectory;
        public string Title;
        public uint Flags;
        public ushort FileOffset, FileExtension;
        public string? DefaultExtension;
        public nint CustomData, Hook, TemplateName, Reserved;
        public int ReservedValue, FlagsEx;
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsChoose(string title)
    {
        nint buffer = Marshal.AllocHGlobal(32768 * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            using var process = Process.GetCurrentProcess();
            var picker = new OpenFileName
            {
                Size = Marshal.SizeOf<OpenFileName>(), Owner = process.MainWindowHandle,
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.tga)\0*.png;*.jpg;*.jpeg;*.bmp;*.tga\0All files (*.*)\0*.*\0\0",
                FilterIndex = 1, File = buffer, MaximumFile = 32768,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Title = title,
                Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008, // Explorer, file/path must exist, no working-directory change.
            };
            if (GetOpenFileNameW(ref picker)) return Marshal.PtrToStringUni(buffer);
            int error = CommDlgExtendedError();
            if (error != 0) throw new IOException($"Windows file chooser failed (0x{error:X}).");
            return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OpenFileName picker);
    [DllImport("comdlg32.dll", ExactSpelling = true)]
    private static extern int CommDlgExtendedError();
}
