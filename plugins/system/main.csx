// Windows system commands and a few read-outs. Keyword: sys
//
//   sys              everything, grouped by how often it gets used
//   sys lock         lock the screen
//   sys dev          Device Manager
//   sys ram          memory, disk, uptime and IP rows — Enter copies the value
//
// Anything that loses work (shut down, restart, sign out, emptying the recycle bin)
// asks first. Things that need elevation say so and trigger the UAC prompt themselves.

using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

// ---------- launching ----------
void Shell(string file, string arguments = "")
    => Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true });

void Hidden(string file, string arguments)
    => Process.Start(new ProcessStartInfo(file, arguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    });

// Verb=runas is what raises the UAC prompt; cancelling it throws, which is not an error here
void Elevated(string file, string arguments)
{
    try
    {
        Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = true,
            Verb = "runas"
        });
    }
    catch
    {
        // the user declined the prompt
    }
}

bool Confirmed(string question)
    => System.Windows.MessageBox.Show(
           question,
           "PlugLauncher",
           System.Windows.MessageBoxButton.YesNo,
           System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

// ---------- the command table ----------
class Command
{
    public string Id = "";
    public string Title = "";
    public string Subtitle = "";
    public string Tags = "";
    public int Weight;              // ties are broken by this, so common things sit higher
    public string? Question;        // when set, Enter asks before doing anything
    public Action Run = () => { };
}

Command Make(string id, string title, string subtitle, string tags, int weight, Action run, string? question = null)
    => new Command { Id = id, Title = title, Subtitle = subtitle, Tags = tags, Weight = weight, Run = run, Question = question };

List<Command>? commands;

List<Command> Commands()
{
    if (commands is not null) return commands;

    commands =
    [
        // ----- power -----
        Make("lock", "Lock the screen", "Keeps everything running, just locks it",
            "lock screen workstation win l", 90,
            () => Hidden("rundll32.exe", "user32.dll,LockWorkStation")),

        Make("sleep", "Sleep", "Suspends to RAM — becomes hibernate if hibernation is enabled",
            "sleep suspend standby", 85,
            () => Hidden("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0")),

        Make("hibernate", "Hibernate", "Writes memory to disk and powers off",
            "hibernate", 60,
            () => Hidden("shutdown.exe", "/h")),

        Make("signout", "Sign out", "Closes every app in this session",
            "sign out log off logout", 55,
            () => Hidden("shutdown.exe", "/l"),
            "Sign out? Anything unsaved will be lost."),

        Make("restart", "Restart", "Reboots now",
            "restart reboot", 70,
            () => Hidden("shutdown.exe", "/r /t 0"),
            "Restart now? Anything unsaved will be lost."),

        Make("shutdown", "Shut down", "Powers off now",
            "shutdown power off turn off", 70,
            () => Hidden("shutdown.exe", "/s /t 0"),
            "Shut down now? Anything unsaved will be lost."),

        Make("abort", "Cancel a pending shutdown", "Only does something if one was scheduled",
            "cancel abort shutdown", 20,
            () => Hidden("shutdown.exe", "/a")),

        // ----- maintenance -----
        Make("recycle", "Empty the Recycle Bin", "Deletes everything in it for good",
            "empty recycle bin trash", 50,
            () => Hidden("powershell.exe", "-NoProfile -Command Clear-RecycleBin -Force -ErrorAction SilentlyContinue"),
            "Empty the Recycle Bin? The files cannot be restored afterwards."),

        Make("explorer", "Restart Explorer", "Fixes a frozen taskbar or desktop",
            "restart explorer taskbar shell", 45,
            () => Hidden("cmd.exe", "/c taskkill /f /im explorer.exe & start explorer.exe")),

        Make("flushdns", "Flush the DNS cache", "Needs admin — a UAC prompt will appear",
            "flush dns cache ipconfig network", 45,
            () => Elevated("cmd.exe", "/c ipconfig /flushdns & pause")),

        // ----- consoles -----
        Make("taskmgr", "Task Manager", "taskmgr", "task manager processes cpu", 80, () => Shell("taskmgr.exe")),
        Make("devmgmt", "Device Manager", "devmgmt.msc", "device manager drivers hardware", 60, () => Shell("devmgmt.msc")),
        Make("services", "Services", "services.msc", "services daemons", 60, () => Shell("services.msc")),
        Make("eventvwr", "Event Viewer", "eventvwr.msc", "event viewer logs", 50, () => Shell("eventvwr.msc")),
        Make("diskmgmt", "Disk Management", "diskmgmt.msc", "disk management partitions volumes", 50, () => Shell("diskmgmt.msc")),
        Make("compmgmt", "Computer Management", "compmgmt.msc", "computer management", 40, () => Shell("compmgmt.msc")),
        Make("regedit", "Registry Editor", "regedit", "registry regedit", 45, () => Shell("regedit.exe")),
        Make("msinfo", "System Information", "msinfo32", "system information specs msinfo", 45, () => Shell("msinfo32.exe")),

        // ----- control panel and settings -----
        Make("envvars", "Environment Variables", "The dialog with PATH in it",
            "environment variables path env", 65,
            () => Hidden("rundll32.exe", "sysdm.cpl,EditEnvironmentVariables")),

        Make("appwiz", "Programs and Features", "appwiz.cpl — uninstall a program",
            "programs features uninstall add remove", 55,
            () => Shell("control.exe", "appwiz.cpl")),

        Make("ncpa", "Network Connections", "ncpa.cpl — adapters",
            "network connections adapters ncpa", 55,
            () => Shell("control.exe", "ncpa.cpl")),

        Make("mmsys", "Sound", "mmsys.cpl — playback and recording devices",
            "sound audio playback microphone", 50,
            () => Shell("control.exe", "mmsys.cpl")),

        Make("powercfg", "Power Options", "powercfg.cpl", "power options battery plan", 40,
            () => Shell("control.exe", "powercfg.cpl")),

        Make("settings", "Windows Settings", "ms-settings:", "settings windows", 50, () => Shell("ms-settings:")),
        Make("update", "Windows Update", "ms-settings:windowsupdate", "windows update", 50, () => Shell("ms-settings:windowsupdate")),
        Make("bluetooth", "Bluetooth and devices", "ms-settings:bluetooth", "bluetooth devices", 40, () => Shell("ms-settings:bluetooth")),
        Make("display", "Display settings", "ms-settings:display", "display screen resolution monitor", 40, () => Shell("ms-settings:display")),
        Make("apps-default", "Default apps", "ms-settings:defaultapps", "default apps browser", 35, () => Shell("ms-settings:defaultapps")),

        // ----- folders -----
        Make("startup", "Startup folder", "Shortcuts here run at sign-in",
            "startup folder autostart", 45, () => Shell("explorer.exe", "shell:startup")),

        Make("appdata", "AppData\\Roaming", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "appdata roaming folder", 40,
            () => Shell("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))),

        Make("temp", "Temp folder", Path.GetTempPath(), "temp folder tmp cache", 40,
            () => Shell("explorer.exe", Path.GetTempPath()))
    ];

    return commands;
}

// ---------- read-outs ----------
[StructLayout(LayoutKind.Sequential)]
struct MemoryStatus
{
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
}

static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}

string Gigabytes(ulong bytes) => $"{bytes / 1024d / 1024d / 1024d:0.0} GB";

// Interfaces are cached briefly: this runs on every keystroke and enumerating them is not free.
List<string>? addresses;
DateTime addressStamp;

List<string> Addresses()
{
    if (addresses is not null && DateTime.UtcNow - addressStamp < TimeSpan.FromSeconds(10)) return addresses;

    var found = new List<string>();

    try
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                found.Add($"{address.Address}  ·  {adapter.Name}");
            }
        }
    }
    catch
    {
        // no adapters is a perfectly ordinary answer here
    }

    addresses = found;
    addressStamp = DateTime.UtcNow;
    return addresses;
}

List<Command> Readouts()
{
    var rows = new List<Command>();

    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
    rows.Add(Make("uptime", $"Uptime: {(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m",
        "Since the last boot  ·  Enter to copy", "uptime boot info", 30,
        () => Clipboard.Copy($"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m")));

    var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
    if (Native.GlobalMemoryStatusEx(ref memory))
    {
        var used = memory.TotalPhys - memory.AvailPhys;
        var text = $"{Gigabytes(used)} of {Gigabytes(memory.TotalPhys)}";
        rows.Add(Make("ram", $"Memory: {text}", $"{memory.MemoryLoad}% in use  ·  Enter to copy",
            "ram memory info", 30, () => Clipboard.Copy(text)));
    }

    foreach (var drive in DriveInfo.GetDrives())
    {
        try
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

            var text = $"{Gigabytes((ulong)drive.AvailableFreeSpace)} free of {Gigabytes((ulong)drive.TotalSize)}";
            rows.Add(Make($"disk-{drive.Name}", $"Disk {drive.Name.TrimEnd('\\')} {text}",
                $"{drive.DriveFormat}  ·  Enter to copy", "disk drive space free info", 28,
                () => Clipboard.Copy(text)));
        }
        catch
        {
            // a drive can go away between GetDrives and reading it
        }
    }

    foreach (var address in Addresses())
    {
        var ip = address.Split(' ')[0];
        rows.Add(Make($"ip-{ip}", $"IP: {address}", "Local address  ·  Enter to copy",
            "ip address network info", 28, () => Clipboard.Copy(ip)));
    }

    rows.Add(Make("machine", $"{Environment.MachineName}  ·  {Environment.UserName}",
        $"{Environment.OSVersion.VersionString}  ·  {Environment.ProcessorCount} cores  ·  Enter to copy",
        "computer name user machine host info", 25,
        () => Clipboard.Copy(Environment.MachineName)));

    return rows;
}

// ---------- scoring ----------
int ScoreOf(Command command, string term)
{
    if (term.Length == 0) return command.Weight;

    // An exact tag comes first on purpose: "ram" is a whole tag on the memory row but also
    // sits inside "Programs and Features", and the memory row is what was asked for.
    foreach (var tag in command.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        if (tag.Equals(term, StringComparison.OrdinalIgnoreCase)) return 120 + command.Weight;
    }

    if (command.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 100 + command.Weight;
    if (command.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) return 70 + command.Weight;

    // tags carry the words people actually type: "reboot" for Restart, "trash" for the bin
    foreach (var tag in command.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        if (tag.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 60 + command.Weight;
    }

    return command.Subtitle.Contains(term, StringComparison.OrdinalIgnoreCase) ? 20 + command.Weight : -1;
}

// ---------- plugin ----------
return Plugin.Create(query =>
{
    var term = query.Search.Trim();

    var all = new List<Command>(Commands());
    all.AddRange(Readouts());

    return all
        .Select(c => new { command = c, score = ScoreOf(c, term) })
        .Where(x => x.score >= 0)
        .OrderByDescending(x => x.score)
        .ThenBy(x => x.command.Title, StringComparer.CurrentCultureIgnoreCase)
        .Take(20)
        .Select(x => new PluginResult
        {
            Id = x.command.Id,
            Title = x.command.Title,
            Subtitle = x.command.Question is null
                ? x.command.Subtitle
                : $"{x.command.Subtitle}  ·  asks first",
            Score = x.score,
            Action = () =>
            {
                if (x.command.Question is not null && !Confirmed(x.command.Question)) return;
                x.command.Run();
            }
        })
        .ToList();
});
