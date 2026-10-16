using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using WaterMgmt.Core;

namespace WaterMgmt.App;

public partial class MainWindow : Window
{
    SiteProject project = new();
    string? projectPath;
    readonly TabControl tabs = new();
    readonly TextBlock status = new() { Margin = new Thickness(8, 4) };

    public MainWindow()
    {
        Title = "Quarry Water Management";
        Width = 1400; Height = 860;
        var root = new DockPanel();
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        tabs.Items.Add(new TabItem { Header = "Site Map", Content = BuildMapTab() });
        tabs.Items.Add(new TabItem { Header = "Features", Content = BuildFeaturesTab() });
        tabs.Items.Add(new TabItem { Header = "Permit & Takings", Content = BuildPermitTab() });
        tabs.Items.Add(new TabItem { Header = "Import Spreadsheet", Content = BuildImportTab() });
        tabs.Items.Add(new TabItem { Header = "Compliance & Reports", Content = BuildReportTab() });
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs) RefreshAll(); };
        root.Children.Add(tabs);
        Content = root;
        RefreshAll();
    }

    Menu BuildMenu()
    {
        MenuItem Item(string h, Action a) { var m = new MenuItem { Header = h }; m.Click += (_, _) => a(); return m; }
        return new Menu
        {
            Items =
            {
                new MenuItem { Header = "_File", Items =
                {
                    Item("_New site", () => { project = new(); projectPath = null; RefreshAll(); }),
                    Item("_Open…", async () => await OpenProject()),
                    Item("_Save", async () => await SaveProject(false)),
                    Item("Save _as…", async () => await SaveProject(true)),
                }},
                new MenuItem { Header = "_Map", Items =
                {
                    Item("Load _satellite image…", async () => await LoadImage()),
                }},
            }
        };
    }

    void RefreshAll()
    {
        Title = $"Quarry Water Management – {project.SiteName}{(projectPath == null ? "" : " (" + Path.GetFileName(projectPath) + ")")}";
        RefreshMap(); RefreshFeatures(); RefreshPermit(); RefreshReport();
    }

    void SetStatus(string s) => status.Text = s;

    async Task<string?> PickOpen(string title, params string[] patterns)
    {
        var r = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(title) { Patterns = patterns } }
        });
        return r.Count == 0 ? null : r[0].TryGetLocalPath();
    }

    async Task<string?> PickSave(string title, string suggested, string ext)
    {
        var r = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggested, DefaultExtension = ext,
            FileTypeChoices = new[] { new FilePickerFileType(ext) { Patterns = new[] { "*." + ext } } }
        });
        return r?.TryGetLocalPath();
    }

    async Task OpenProject()
    {
        var p = await PickOpen("Water site file", "*.wsite");
        if (p == null) return;
        try { project = ProjectStorage.Load(p); projectPath = p; RefreshAll(); SetStatus("Opened " + p); }
        catch (Exception ex) { SetStatus("Could not open: " + ex.Message); }
    }

    async Task SaveProject(bool asNew)
    {
        if (asNew || projectPath == null)
            projectPath = await PickSave("Save site", project.SiteName, "wsite") ?? projectPath;
        if (projectPath == null) return;
        ProjectStorage.Save(project, projectPath);
        Title = $"Quarry Water Management – {project.SiteName} ({Path.GetFileName(projectPath)})";
        SetStatus("Saved " + projectPath);
    }

    async Task LoadImage()
    {
        var p = await PickOpen("Satellite image", "*.png", "*.jpg", "*.jpeg", "*.bmp");
        if (p == null) return;
        project.ImagePath = p; RefreshMap(); SetStatus("Background set: " + p);
    }

    // small layout helpers
    static StackPanel Row(params Control[] c)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4) };
        s.Children.AddRange(c); return s;
    }
    static Button Btn(string t, Action a) { var b = new Button { Content = t }; b.Click += (_, _) => a(); return b; }
    static TextBlock Lbl(string t) => new() { Text = t, VerticalAlignment = VerticalAlignment.Center };
}
