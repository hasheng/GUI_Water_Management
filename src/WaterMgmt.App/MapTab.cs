using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using WaterMgmt.Core;

namespace WaterMgmt.App;

public partial class MainWindow
{
    const double NodeW = 100, NodeH = 44;
    readonly Canvas map = new() { Background = Brushes.Transparent, Width = 1600, Height = 1000, Focusable = true };
    readonly List<Control> linkShapes = new();
    readonly Dictionary<Guid, Border> nodeViews = new();
    FeatureKind? tool = FeatureKind.Pit;   // null = select, handled via toolMode
    string toolMode = "select";            // select | add | pipe
    Guid? selNode, selLink, pipeStart;
    bool loadingProps;
    HashSet<Guid> problemNodes = new();
    TextBox pName = new(), pNotes = new() { AcceptsReturn = true, Height = 70 }, pRated = new(), pOtherIn = new(), pOtherLoss = new(), pFlow = new(), pLabel = new();
    CheckBox pDewater = new() { Content = "Actively dewatered" };
    ComboBox pSource = new() { MinWidth = 200 };
    ComboBox pKind = new() { ItemsSource = Enum.GetValues<FeatureKind>(), MinWidth = 200 };
    StackPanel nodePanel = new(), linkPanel = new();
    TextBlock toolHint = new() { Margin = new Thickness(0, 6), TextWrapping = TextWrapping.Wrap };

    static IBrush KindBrush(FeatureKind k) => k switch
    {
        FeatureKind.Pit => Brushes.SteelBlue,
        FeatureKind.StormPond => Brushes.CadetBlue,
        FeatureKind.Sump => Brushes.SlateBlue,
        FeatureKind.Well => Brushes.DarkCyan,
        FeatureKind.WashStation => Brushes.Orange,
        FeatureKind.Pump => Brushes.SeaGreen,
        FeatureKind.Outfall => Brushes.Firebrick,
        _ => Brushes.Gray
    };

    Control BuildMapTab()
    {
        var left = new StackPanel { Spacing = 4, Margin = new Thickness(8), Width = 150 };
        left.Children.Add(Lbl("Tools"));
        left.Children.Add(Btn("Select / move", () => SetTool("select", null)));
        foreach (var k in Enum.GetValues<FeatureKind>())
        {
            var kk = k; left.Children.Add(Btn("+ " + k, () => SetTool("add", kk)));
        }
        left.Children.Add(Btn("→ Pipe (flow)", () => SetTool("pipe", null)));
        left.Children.Add(toolHint);
        left.Children.Add(Btn("Delete selected", DeleteSelected));
        SetTool("select", null);

        // properties
        pKind.SelectionChanged += (_, _) => ApplyNodeProps();
        pName.LostFocus += (_, _) => ApplyNodeProps(); pNotes.LostFocus += (_, _) => ApplyNodeProps();
        pRated.LostFocus += (_, _) => ApplyNodeProps(); pOtherIn.LostFocus += (_, _) => ApplyNodeProps(); pOtherLoss.LostFocus += (_, _) => ApplyNodeProps();
        pDewater.IsCheckedChanged += (_, _) => ApplyNodeProps(); pSource.SelectionChanged += (_, _) => ApplyNodeProps();
        pFlow.LostFocus += (_, _) => ApplyLinkProps(); pLabel.LostFocus += (_, _) => ApplyLinkProps();
        nodePanel.Spacing = 4;
        foreach (var c in new Control[] { Lbl("Name"), pName, Lbl("Type"), pKind, pDewater, Lbl("Rated capacity (L/min)"), pRated, Lbl("Other inflow: seepage/runoff (L/min)"), pOtherIn, Lbl("Other loss: evap/wash use (L/min)"), pOtherLoss,
                     Lbl("Permit source"), pSource, Lbl("Notes"), pNotes }) nodePanel.Children.Add(c);
        linkPanel.Spacing = 4;
        foreach (var c in new Control[] { Lbl("Pipe / channel"), Lbl("Flow (L/min)"), pFlow, Lbl("Label"), pLabel,
                     Btn("Reverse direction", ReverseLink) }) linkPanel.Children.Add(c);
        var props = new StackPanel { Margin = new Thickness(8), Width = 260, Spacing = 8 };
        props.Children.Add(new TextBlock { Text = "Properties", FontWeight = FontWeight.Bold });
        props.Children.Add(nodePanel); props.Children.Add(linkPanel);
        nodePanel.IsVisible = linkPanel.IsVisible = false;

        map.PointerPressed += MapPressed;
        map.KeyDown += (_, e) => { if (e.Key == Key.Delete) DeleteSelected(); };
        var dock = new DockPanel();
        DockPanel.SetDock(left, Dock.Left); DockPanel.SetDock(props, Dock.Right);
        dock.Children.Add(left); dock.Children.Add(props);
        dock.Children.Add(new ScrollViewer
        {
            Content = map, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
        return dock;
    }

    void SetTool(string mode, FeatureKind? kind)
    {
        toolMode = mode; tool = kind; pipeStart = null;
        toolHint.Text = mode switch
        {
            "add" => $"Click on the map to place a {kind}.",
            "pipe" => "Click the source object, then the destination. Arrow shows flow direction.",
            _ => "Drag objects to move them. Click an object or pipe to edit. Delete key removes."
        };
    }

    void RefreshMap()
    {
        map.Children.Clear(); nodeViews.Clear(); linkShapes.Clear();
        try
        {
            if (project.ImagePath != null && File.Exists(project.ImagePath))
            {
                var bmp = new Bitmap(project.ImagePath);
                map.Width = Math.Max(1600, bmp.PixelSize.Width); map.Height = Math.Max(1000, bmp.PixelSize.Height);
                map.Children.Add(new Image { Source = bmp, IsHitTestVisible = false, Width = bmp.PixelSize.Width, Height = bmp.PixelSize.Height });
            }
        }
        catch (Exception ex) { SetStatus("Image error: " + ex.Message); }
        problemNodes = MassBalance.Compute(project, BalanceTolerance).Where(n => MassBalance.IsProblem(n.Status)).Select(n => n.Feature.Id).ToHashSet();
        foreach (var f in project.Features) AddNodeView(f);
        DrawLinks();
        ShowProps();
    }

    void AddNodeView(SiteFeature f)
    {
        var text = new TextBlock
        {
            Text = f.Name + (f.Dewatered ? " 💧" : ""), Foreground = Brushes.White, FontSize = 12,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var b = new Border
        {
            Width = NodeW, Height = NodeH, Background = KindBrush(f.Kind), Child = text, Cursor = new Cursor(StandardCursorType.Hand),
            CornerRadius = new CornerRadius(f.Kind == FeatureKind.Pump ? 22 : 6), Padding = new Thickness(4),
            BorderThickness = new Thickness(f.Id == selNode || problemNodes.Contains(f.Id) ? 3 : 1), BorderBrush = f.Id == selNode ? Brushes.Gold : problemNodes.Contains(f.Id) ? Brushes.Red : Brushes.White,
            ZIndex = 10, Tag = f
        };
        Canvas.SetLeft(b, f.X); Canvas.SetTop(b, f.Y);
        Point? grab = null;
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true; map.Focus();
            if (toolMode == "pipe") { PipeClick(f); return; }
            SelectNode(f.Id);
            grab = e.GetPosition(b); e.Pointer.Capture(b);
        };
        b.PointerMoved += (_, e) =>
        {
            if (grab == null) return;
            var p = e.GetPosition(map);
            f.X = Math.Max(0, p.X - grab.Value.X); f.Y = Math.Max(0, p.Y - grab.Value.Y);
            Canvas.SetLeft(b, f.X); Canvas.SetTop(b, f.Y); DrawLinks();
        };
        b.PointerReleased += (_, e) => { grab = null; e.Pointer.Capture(null); };
        map.Children.Add(b); nodeViews[f.Id] = b;
    }

    void MapPressed(object? s, PointerPressedEventArgs e)
    {
        map.Focus();
        var p = e.GetPosition(map);
        if (toolMode == "add" && tool is { } k)
        {
            var n = project.Features.Count(x => x.Kind == k) + 1;
            var f = new SiteFeature { Kind = k, Name = $"{k} {n}", X = p.X - NodeW / 2, Y = p.Y - NodeH / 2 };
            project.Features.Add(f); selNode = f.Id; selLink = null; RefreshMap();
        }
        else if (toolMode == "select") { selNode = null; selLink = null; RefreshMap(); }
    }

    void PipeClick(SiteFeature f)
    {
        if (pipeStart == null) { pipeStart = f.Id; SetStatus($"Pipe from {f.Name}: now click the destination."); return; }
        if (pipeStart != f.Id)
        {
            var l = new FlowLink { FromId = pipeStart.Value, ToId = f.Id };
            project.Links.Add(l); selLink = l.Id; selNode = null; DrawLinks(); ShowProps();
            SetStatus("Pipe added.");
        }
        pipeStart = null;
    }

    static Point Center(SiteFeature f) => new(f.X + NodeW / 2, f.Y + NodeH / 2);

    /// <summary>Point where the ray from 'from' toward the rect centre of 'to' meets the rect edge.</summary>
    static Point EdgePoint(Point from, SiteFeature to)
    {
        var c = Center(to); double dx = from.X - c.X, dy = from.Y - c.Y;
        if (dx == 0 && dy == 0) return c;
        double sx = dx == 0 ? double.MaxValue : (NodeW / 2 + 3) / Math.Abs(dx);
        double sy = dy == 0 ? double.MaxValue : (NodeH / 2 + 3) / Math.Abs(dy);
        double s = Math.Min(sx, sy);
        return new Point(c.X + dx * s, c.Y + dy * s);
    }

    void DrawLinks()
    {
        foreach (var c in linkShapes) map.Children.Remove(c);
        linkShapes.Clear();
        foreach (var l in project.Links.ToList())
        {
            var a = project.Features.FirstOrDefault(x => x.Id == l.FromId);
            var b = project.Features.FirstOrDefault(x => x.Id == l.ToId);
            if (a == null || b == null) { project.Links.Remove(l); continue; }
            var sel = l.Id == selLink;
            var brush = sel ? Brushes.Gold : Brushes.DodgerBlue;
            var start = EdgePoint(Center(b), a); var end = EdgePoint(Center(a), b);
            var line = new Line { StartPoint = start, EndPoint = end, Stroke = brush, StrokeThickness = sel ? 5 : 3, ZIndex = 1 };
            var ang = Math.Atan2(end.Y - start.Y, end.X - start.X); const double len = 14, wing = 0.45;
            var head = new Polygon
            {
                Fill = brush, ZIndex = 2,
                Points = new[] { end,
                    new Point(end.X - len * Math.Cos(ang - wing), end.Y - len * Math.Sin(ang - wing)),
                    new Point(end.X - len * Math.Cos(ang + wing), end.Y - len * Math.Sin(ang + wing)) }
            };
            var id = l.Id;
            void Pick(object? _, PointerPressedEventArgs e)
            { if (toolMode == "pipe") return; e.Handled = true; map.Focus(); selLink = id; selNode = null; RefreshMap(); }
            line.PointerPressed += Pick; head.PointerPressed += Pick;
            map.Children.Add(line); map.Children.Add(head); linkShapes.Add(line); linkShapes.Add(head);
            var txt = (l.Label + (l.FlowLpm is { } q ? $" {q:0} L/min" : "")).Trim();
            if (txt.Length > 0)
            {
                var t = new TextBlock { Text = txt, Background = Brushes.White, FontSize = 11, ZIndex = 3, IsHitTestVisible = false };
                Canvas.SetLeft(t, (start.X + end.X) / 2 - 20); Canvas.SetTop(t, (start.Y + end.Y) / 2 - 8);
                map.Children.Add(t); linkShapes.Add(t);
            }
        }
    }

    void SelectNode(Guid id)
    {
        selNode = id; selLink = null;
        foreach (var (nid, v) in nodeViews)
        { v.BorderThickness = new Thickness(nid == id ? 3 : 1); v.BorderBrush = nid == id ? Brushes.Gold : problemNodes.Contains(nid) ? Brushes.Red : Brushes.White; }
        DrawLinks(); ShowProps();
    }

    void ShowProps()
    {
        loadingProps = true;
        var f = project.Features.FirstOrDefault(x => x.Id == selNode);
        var l = project.Links.FirstOrDefault(x => x.Id == selLink);
        nodePanel.IsVisible = f != null; linkPanel.IsVisible = l != null;
        if (f != null)
        {
            pName.Text = f.Name; pKind.SelectedItem = f.Kind; pDewater.IsChecked = f.Dewatered;
            pRated.Text = f.RatedLpm?.ToString(); pOtherIn.Text = f.OtherInflowLpm?.ToString(); pOtherLoss.Text = f.OtherLossLpm?.ToString(); pNotes.Text = f.Notes;
            var names = new List<string> { "(none)" }; names.AddRange(project.Permit.Sources.Select(s => s.Name));
            pSource.ItemsSource = names;
            var idx = project.Permit.Sources.FindIndex(s => s.Id == f.PermitSourceId);
            pSource.SelectedIndex = idx + 1;
        }
        if (l != null) { pFlow.Text = l.FlowLpm?.ToString(); pLabel.Text = l.Label; }
        loadingProps = false;
    }

    void ApplyNodeProps()
    {
        if (loadingProps) return;
        var f = project.Features.FirstOrDefault(x => x.Id == selNode); if (f == null) return;
        f.Name = pName.Text ?? ""; f.Notes = pNotes.Text ?? ""; f.Dewatered = pDewater.IsChecked == true;
        if (pKind.SelectedItem is FeatureKind k) f.Kind = k;
        f.RatedLpm = double.TryParse(pRated.Text, out var r) ? r : null;
        f.OtherInflowLpm = double.TryParse(pOtherIn.Text, out var oi) ? oi : null; f.OtherLossLpm = double.TryParse(pOtherLoss.Text, out var ol) ? ol : null;
        var i = pSource.SelectedIndex;
        f.PermitSourceId = i > 0 && i - 1 < project.Permit.Sources.Count ? project.Permit.Sources[i - 1].Id : null;
        RefreshMap();
    }

    void ApplyLinkProps()
    {
        if (loadingProps) return;
        var l = project.Links.FirstOrDefault(x => x.Id == selLink); if (l == null) return;
        l.FlowLpm = double.TryParse(pFlow.Text, out var q) ? q : null; l.Label = pLabel.Text ?? "";
        DrawLinks();
    }

    void ReverseLink()
    {
        var l = project.Links.FirstOrDefault(x => x.Id == selLink); if (l == null) return;
        (l.FromId, l.ToId) = (l.ToId, l.FromId); DrawLinks();
    }

    void DeleteSelected()
    {
        if (selNode != null)
        {
            project.Features.RemoveAll(f => f.Id == selNode);
            project.Links.RemoveAll(l => l.FromId == selNode || l.ToId == selNode);
        }
        else if (selLink != null) project.Links.RemoveAll(l => l.Id == selLink);
        selNode = selLink = null; RefreshMap();
    }
}
