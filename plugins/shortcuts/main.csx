// User-defined launch aliases. Keywords: go, shortcut
//
//   go                  -> every shortcut, plus the manager
//   go editor           -> shortcuts matching "editor"
//   shortcut manage     -> the dedicated add/edit/remove window
//
// The JSON belongs to the plugin and lives in its data directory. The management window uses the
// supported PlugLauncher.PluginUI layer: no app internals, dispatcher guessing, or leaked windows.

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PlugLauncher.PluginUI;

sealed class UserShortcut
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Program { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
}

string DataFile = "";
IPluginLogger? Log = null;
PluginWindowScope? Windows = null;
List<UserShortcut> Shortcuts = [];
readonly object Gate = new();
readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

UserShortcut Copy(UserShortcut item) => new()
{
    Id = item.Id,
    Name = item.Name,
    Program = item.Program,
    Arguments = item.Arguments,
    WorkingDirectory = item.WorkingDirectory
};

List<UserShortcut> Snapshot()
{
    lock (Gate) return Shortcuts.Select(Copy).ToList();
}

void Load()
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataFile)!);
        if (!File.Exists(DataFile)) return;

        var loaded = JsonSerializer.Deserialize<List<UserShortcut>>(File.ReadAllText(DataFile), JsonOptions) ?? [];
        foreach (var item in loaded)
        {
            item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
            item.Name = (item.Name ?? "").Trim();
            item.Program = (item.Program ?? "").Trim();
            item.Arguments = (item.Arguments ?? "").Trim();
            item.WorkingDirectory = (item.WorkingDirectory ?? "").Trim();
        }

        lock (Gate)
            Shortcuts = loaded
                .Where(item => item.Name.Length > 0 && item.Program.Length > 0)
                .GroupBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => group.First())
                .ToList();
    }
    catch (Exception ex)
    {
        Log?.Error("could not load shortcuts", ex);
    }
}

bool TrySave(List<UserShortcut> next, out string problem)
{
    var temporary = DataFile + ".tmp";
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataFile)!);
        var ordered = next.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        File.WriteAllText(temporary, JsonSerializer.Serialize(ordered, JsonOptions));
        File.Move(temporary, DataFile, overwrite: true);

        lock (Gate) Shortcuts = ordered.Select(Copy).ToList();
        problem = "";
        return true;
    }
    catch (Exception ex)
    {
        try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        Log?.Error("could not save shortcuts", ex);
        problem = ex.Message;
        return false;
    }
}

string Expanded(string value) => Environment.ExpandEnvironmentVariables(value.Trim());

string CommandLine(UserShortcut item)
    => item.Arguments.Length == 0 ? item.Program : item.Program + " " + item.Arguments;

void Launch(UserShortcut item)
{
    var info = new ProcessStartInfo
    {
        FileName = Expanded(item.Program),
        Arguments = Environment.ExpandEnvironmentVariables(item.Arguments),
        UseShellExecute = true
    };

    if (item.WorkingDirectory.Length > 0)
        info.WorkingDirectory = Expanded(item.WorkingDirectory);

    Process.Start(info);
}

TextBlock Text(string value, double size = 13, IBrush? colour = null) => new()
{
    Text = value,
    FontSize = size,
    Foreground = colour ?? PluginWindow.ResourceBrush("TextBrush", Color.FromRgb(0xF2, 0xF2, 0xF2)),
    TextTrimming = TextTrimming.CharacterEllipsis
};

Button Button(string label, string style = "ghost")
{
    var button = new Button { Content = label, MinWidth = 82 };
    button.Classes.Add(style);
    return button;
}

TextBox Field(string text = "")
{
    var field = new TextBox { Text = text };
    field.Classes.Add("field");
    return field;
}

StackPanel FormField(string label, Control input, string? hint = null)
{
    var panel = new StackPanel { Spacing = 5 };
    panel.Children.Add(Text(label, 12,
        PluginWindow.ResourceBrush("SubtleTextBrush", Color.FromRgb(0x9A, 0x9A, 0x9A))));
    panel.Children.Add(input);
    if (!string.IsNullOrWhiteSpace(hint))
        panel.Children.Add(Text(hint, 11,
            PluginWindow.ResourceBrush("HintTextBrush", Color.FromRgb(0x6E, 0x6E, 0x6E))));
    return panel;
}

async Task<UserShortcut?> EditShortcut(Window owner, UserShortcut? original, IReadOnlyList<UserShortcut> all)
{
    var editor = new PluginWindow
    {
        Title = original is null ? "Add shortcut" : "Edit shortcut",
        Width = 620,
        Height = 520,
        MinWidth = 620,
        MinHeight = 520,
        CanResize = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };

    var name = Field(original?.Name ?? "");
    var program = Field(original?.Program ?? "");
    var arguments = Field(original?.Arguments ?? "");
    var workingDirectory = Field(original?.WorkingDirectory ?? "");
    var error = Text("", 12,
        PluginWindow.ResourceBrush("DangerBrush", Color.FromRgb(0xE0, 0x5B, 0x5B)));

    var browseProgram = Button("Browse…");
    var programRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
    programRow.Children.Add(program);
    Grid.SetColumn(browseProgram, 1);
    programRow.Children.Add(browseProgram);

    var browseDirectory = Button("Browse…");
    var directoryRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
    directoryRow.Children.Add(workingDirectory);
    Grid.SetColumn(browseDirectory, 1);
    directoryRow.Children.Add(browseDirectory);

    browseProgram.Click += async (_, _) =>
    {
        var picked = await editor.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a program or file",
            AllowMultiple = false
        });
        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) program.Text = path;
    };

    browseDirectory.Click += async (_, _) =>
    {
        var picked = await editor.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a working folder",
            AllowMultiple = false
        });
        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) workingDirectory.Text = path;
    };

    var cancel = Button("Cancel");
    var save = Button("Save", "pill");
    save.IsDefault = true;
    cancel.IsCancel = true;

    cancel.Click += (_, _) => editor.Close(null);
    save.Click += (_, _) =>
    {
        var shortcutName = (name.Text ?? "").Trim();
        var target = (program.Text ?? "").Trim();

        if (shortcutName.Length == 0)
        {
            error.Text = "Give the shortcut a name.";
            name.Focus();
            return;
        }

        if (target.Length == 0)
        {
            error.Text = "Choose a program, file or URL to open.";
            program.Focus();
            return;
        }

        if (all.Any(item => item.Id != original?.Id &&
                            item.Name.Equals(shortcutName, StringComparison.CurrentCultureIgnoreCase)))
        {
            error.Text = $"A shortcut named “{shortcutName}” already exists.";
            name.Focus();
            return;
        }

        editor.Close(new UserShortcut
        {
            Id = original?.Id ?? Guid.NewGuid().ToString("N"),
            Name = shortcutName,
            Program = target,
            Arguments = (arguments.Text ?? "").Trim(),
            WorkingDirectory = (workingDirectory.Text ?? "").Trim()
        });
    };

    var form = new StackPanel { Spacing = 14 };
    form.Children.Add(Text(original is null ? "Add a shortcut" : "Edit the shortcut", 22));
    form.Children.Add(Text("Type its name after “go” in the launcher to run it.", 12,
        PluginWindow.ResourceBrush("SubtleTextBrush", Color.FromRgb(0x9A, 0x9A, 0x9A))));
    form.Children.Add(FormField("Name", name, "For example: editor, work browser, music"));
    form.Children.Add(FormField("Program, file or URL", programRow));
    form.Children.Add(FormField("Arguments (optional)", arguments));
    form.Children.Add(FormField("Working folder (optional)", directoryRow));
    form.Children.Add(error);

    var buttons = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 8,
        Children = { cancel, save }
    };

    var root = new DockPanel { Margin = new Thickness(24, 22, 24, 20) };
    DockPanel.SetDock(buttons, Dock.Bottom);
    root.Children.Add(buttons);
    root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
    editor.Content = root;
    editor.Opened += (_, _) => name.Focus();

    return await editor.ShowDialog<UserShortcut?>(owner);
}

Window BuildManager()
{
    var window = new PluginWindow
    {
        Title = "Custom Shortcuts",
        Width = 760,
        Height = 560,
        MinWidth = 560,
        MinHeight = 400
    };

    var list = new ListBox { SelectionMode = SelectionMode.Single };
    list.Classes.Add("results");

    var status = Text("", 12,
        PluginWindow.ResourceBrush("SubtleTextBrush", Color.FromRgb(0x9A, 0x9A, 0x9A)));
    var add = Button("Add shortcut", "pill");
    var edit = Button("Edit");
    var remove = Button("Remove", "ghost");
    remove.Classes.Add("danger");
    edit.IsEnabled = false;
    remove.IsEnabled = false;

    string? SelectedId() => (list.SelectedItem as ListBoxItem)?.Tag as string;

    void RefreshRows(string? selectId = null)
    {
        var rows = new List<ListBoxItem>();
        ListBoxItem? selected = null;

        foreach (var shortcut in Snapshot().OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var content = new StackPanel { Spacing = 3 };
            content.Children.Add(Text(shortcut.Name, 14));
            content.Children.Add(Text(CommandLine(shortcut), 11,
                PluginWindow.ResourceBrush("SubtleTextBrush", Color.FromRgb(0x9A, 0x9A, 0x9A))));

            var row = new ListBoxItem { Tag = shortcut.Id, Content = content };
            rows.Add(row);
            if (shortcut.Id == selectId) selected = row;
        }

        list.ItemsSource = rows;
        list.SelectedItem = selected;
        status.Text = rows.Count == 0
            ? "No shortcuts yet. Add one to make it available from the launcher."
            : $"{rows.Count} shortcut{(rows.Count == 1 ? "" : "s")} · changes are saved immediately";
    }

    async Task EditSelected()
    {
        var id = SelectedId();
        if (id is null) return;

        var all = Snapshot();
        var original = all.FirstOrDefault(item => item.Id == id);
        if (original is null) return;

        var changed = await EditShortcut(window, original, all);
        if (changed is null) return;

        var index = all.FindIndex(item => item.Id == id);
        if (index >= 0) all[index] = changed;

        if (!TrySave(all, out var problem))
        {
            status.Text = "Could not save: " + problem;
            return;
        }

        RefreshRows(changed.Id);
    }

    list.SelectionChanged += (_, _) =>
    {
        var hasSelection = SelectedId() is not null;
        edit.IsEnabled = hasSelection;
        remove.IsEnabled = hasSelection;
    };
    list.DoubleTapped += async (_, _) => await EditSelected();

    add.Click += async (_, _) =>
    {
        var all = Snapshot();
        var added = await EditShortcut(window, null, all);
        if (added is null) return;
        all.Add(added);

        if (!TrySave(all, out var problem))
        {
            status.Text = "Could not save: " + problem;
            return;
        }

        RefreshRows(added.Id);
    };

    edit.Click += async (_, _) => await EditSelected();
    remove.Click += (_, _) =>
    {
        var id = SelectedId();
        var all = Snapshot();
        var selected = all.FirstOrDefault(item => item.Id == id);
        if (selected is null) return;
        if (!Ask.Confirm("Remove shortcut", $"Remove “{selected.Name}”?")) return;

        all.RemoveAll(item => item.Id == selected.Id);
        if (!TrySave(all, out var problem))
        {
            status.Text = "Could not save: " + problem;
            return;
        }

        RefreshRows();
    };

    var heading = new StackPanel { Spacing = 4 };
    heading.Children.Add(Text("Custom Shortcuts", 24));
    heading.Children.Add(Text("Named programs, files and URLs you can launch with “go”.", 12,
        PluginWindow.ResourceBrush("SubtleTextBrush", Color.FromRgb(0x9A, 0x9A, 0x9A))));

    var actions = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 8,
        Children = { add, edit, remove }
    };

    var root = new DockPanel { Margin = new Thickness(24, 22, 24, 18) };
    DockPanel.SetDock(heading, Dock.Top);
    DockPanel.SetDock(actions, Dock.Bottom);
    DockPanel.SetDock(status, Dock.Bottom);
    heading.Margin = new Thickness(0, 0, 0, 16);
    actions.Margin = new Thickness(0, 12, 0, 0);
    status.Margin = new Thickness(0, 10, 0, 0);
    root.Children.Add(heading);
    root.Children.Add(actions);
    root.Children.Add(status);
    root.Children.Add(list);
    window.Content = root;

    RefreshRows();
    return window;
}

void ShowManager() => Windows!.Show("manager", BuildManager);

int Score(UserShortcut item, string term)
{
    if (item.Name.Equals(term, StringComparison.CurrentCultureIgnoreCase)) return 900;
    if (item.Name.StartsWith(term, StringComparison.CurrentCultureIgnoreCase)) return 760;
    if (item.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)) return 620;
    if (item.Program.Contains(term, StringComparison.CurrentCultureIgnoreCase)) return 400;
    return -1;
}

IReadOnlyList<PluginResult> Query(PluginQuery query)
{
    var term = query.Search.Trim();
    var results = new List<PluginResult>();
    var manageMatch = term.Length == 0 || "manage".StartsWith(term, StringComparison.OrdinalIgnoreCase) ||
                      "settings".StartsWith(term, StringComparison.OrdinalIgnoreCase);

    if (manageMatch)
    {
        var count = Snapshot().Count;
        results.Add(new PluginResult
        {
            Id = "manage",
            Title = count == 0 ? "Add your first shortcut" : "Manage shortcuts",
            Subtitle = count == 0
                ? "open the add, edit and remove window"
                : $"{count} saved · add, edit or remove",
            Score = term.Length == 0 ? 1000 : 850,
            Action = ShowManager
        });
    }

    foreach (var shortcut in Snapshot()
                 .Select(item => new { Item = item, Score = term.Length == 0 ? 600 : Score(item, term) })
                 .Where(match => match.Score >= 0)
                 .OrderByDescending(match => match.Score)
                 .ThenBy(match => match.Item.Name, StringComparer.CurrentCultureIgnoreCase)
                 .Take(20))
    {
        var copy = Copy(shortcut.Item);
        var icon = Expanded(copy.Program);
        results.Add(new PluginResult
        {
            Id = copy.Id,
            Title = copy.Name,
            Subtitle = CommandLine(copy),
            IconPath = File.Exists(icon) ? icon : null,
            Score = shortcut.Score,
            Action = () => Launch(copy)
        });
    }

    if (results.Count == 0)
    {
        results.Add(new PluginResult
        {
            Id = "no-match",
            Title = $"No shortcut matches “{term}”",
            Subtitle = "Enter to open the shortcut manager",
            Score = 100,
            Action = ShowManager
        });
    }

    return results;
}

return Plugin.Create(
    query: (query, cancellationToken) => Task.FromResult(Query(query)),
    initialize: (context, cancellationToken) =>
    {
        DataFile = Path.Combine(context.DataDirectory, "shortcuts.json");
        Log = context.Log;
        Windows = PluginWindows.For(context);
        Load();
        return Task.CompletedTask;
    });
