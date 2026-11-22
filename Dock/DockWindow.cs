using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Perch.Core;
using Perch.Widgets;

namespace Perch.Dock;

/// <summary>A dock item: one widget tile or one app icon, with the magnification transform.</summary>
public sealed class Slot : Border
{
    public Widget? Widget;
    public AppIcon? App;
    public bool Magnifies;
    public double Current = 1;
    public readonly ScaleTransform ScaleT = new(1, 1);
    public string Label => Widget?.Name ?? App?.Label ?? "";
    public bool ShowsLabel => App != null || (Widget?.IsIcon ?? false);
}

public sealed partial class DockWindow : Window, IDockHost
{
    readonly GlassWindow _glass = new();
    readonly Grid _root = new();
    readonly Grid _dock = new() { ClipToBounds = false };
    readonly Border _bar = new();
    readonly StackPanel _items = new() { Orientation = Orientation.Horizontal, ClipToBounds = false };
    readonly Canvas _overlay = new() { IsHitTestVisible = false };
    readonly Border _label = new() { CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 4, 9, 5), Visibility = Visibility.Collapsed };
    readonly TextBlock _labelText;
    readonly List<Slot> _slots = [];
    readonly List<Widget> _widgets = [];
    readonly List<string> _itemIds = [];
    StackPanel? _apps;
    readonly PreviewWindow _preview = new();

    IntPtr _hwnd;
    MonitorInfo _mon = Monitors.Pick(0);
    double _scale = 1;
    bool _appBar;
    uint _appBarMsg, _taskbarCreatedMsg;
    Native.RECT _windowRect;

    Popup? _card;
    Widget? _cardWidget, _lastClosedCard;
    DateTime _lastClosedAt;

    double _mouseX = double.NaN;
    bool _rendering;
    Slot? _hover;

    // Auto-hide and full-screen state
    bool _autoHidden, _fullscreenHidden;
    double _slide; // 0 = shown, 1 = hidden
    double _slideTarget;
    DateTime _lastInside = DateTime.Now;
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _second = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    readonly DispatcherTimer _previewHide = new() { Interval = TimeSpan.FromMilliseconds(250) };
    Slot? _previewSlot;
    int _pollCount;

    // Dragging
    Slot? _pressSlot;
    Point _pressPoint;
    bool _dragged;

    public IntPtr Handle => _hwnd;
    public bool AtTop => Settings.Current.Edge == "top";
    double BarHeight => Settings.Current.IconSize + Settings.Current.Padding * 2;
    double Headroom => (Settings.Current.Magnify ? Settings.Current.IconSize * (Settings.Current.MagnifyScale - 1) : 0) + 40;

    public DockWindow()
    {
        Title = "Perch";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Left = -32000;
        Top = -32000;
        Width = 400;
        Height = 120;
        FontFamily = (FontFamily)Application.Current.Resources["F.Ui"];
        UseLayoutRounding = true;

        _labelText = Ui.Text("", 12, FontWeights.SemiBold, "Card.Text");
        _label.Child = _labelText;
        _label.SetResourceReference(Border.BackgroundProperty, "Card.Fill");
        _label.SetResourceReference(Border.BorderBrushProperty, "Card.Stroke");
        _label.BorderThickness = new Thickness(1);
        _overlay.Children.Add(_label);

        _bar.BorderThickness = new Thickness(1);
        _bar.SetResourceReference(Border.BorderBrushProperty, "Dock.Stroke");
        _dock.Children.Add(_bar);
        _dock.Children.Add(_items);
        _root.Children.Add(_dock);
        _root.Children.Add(_overlay);
        Content = _root;

        _items.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // keeps hover continuous between icons
        _items.MouseMove += (_, e) => { _mouseX = e.GetPosition(_items).X; StartRendering(); };
        _dock.MouseLeave += (_, _) => { _mouseX = double.NaN; StartRendering(); SetHover(null); };
        _bar.MouseRightButtonUp += (_, e) => { ShowDockMenu(); e.Handled = true; };
        _bar.MouseLeftButtonUp += (_, _) => CloseCard();
        _items.MouseRightButtonUp += (_, e) => { if (!e.Handled) { ShowDockMenu(); e.Handled = true; } };
        _items.AllowDrop = true;
        _items.DragOver += OnDragOver;
        _items.Drop += OnDrop;
        _bar.SizeChanged += (_, _) => Dispatcher.BeginInvoke(UpdateGlass, DispatcherPriority.Render);

        Deactivated += (_, _) => CloseCard();
        _poll.Tick += (_, _) => Poll();
        _second.Tick += (_, _) => { foreach (var w in _widgets) { try { w.Tick(); } catch { } } };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); ShowPreview(); };
        _previewHide.Tick += (_, _) => CheckPreviewHide();
        _preview.Chosen += () => _previewSlot = null;
        _preview.MouseLeave += (_, _) => _previewHide.Start();

        WindowTracker.Instance.Changed += RefreshApps;
        Settings.Changed += rebuild =>
        {
            Theme.Apply();
            if (rebuild) Rebuild();
            else { ApplyChrome(); RefreshApps(); UpdateGlass(); }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW);
        int noPeek = 1;
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_EXCLUDED_FROM_PEEK, ref noPeek, 4);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        _appBarMsg = Native.RegisterWindowMessage("Perch.AppBar");
        _taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
    }

    /// <summary>Shows the dock for the first time.</summary>
    public void Start()
    {
        _glass.Show();
        Owner = _glass; // the dock always stays above its glass
        Show();
        _preview.Owner = this;
        if (Settings.Current.HideTaskbar) Taskbar.Hide();
        Rebuild();
        _poll.Start();
        _second.Start();
    }

    // ───────────────────────── Building ─────────────────────────

    public void Rebuild()
    {
        CloseCard();
        HidePreview();
        foreach (var w in _widgets) { try { w.Detach(); } catch { } }
        _widgets.Clear();
        _slots.Clear();
        _itemIds.Clear();
        _items.Children.Clear();
        _apps = null;
        _appIcons.Clear();

        var s = Settings.Current;
        foreach (var id in s.Items)
        {
            if (id == "apps")
            {
                if (_apps != null) continue;
                _apps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = AtTop ? VerticalAlignment.Top : VerticalAlignment.Bottom, ClipToBounds = false };
                _items.Children.Add(_apps);
                _itemIds.Add(id);
                continue;
            }
            var w = WidgetRegistry.Create(id);
            if (w == null || (id != "sep" && _widgets.Any(x => x.Id == id))) continue;
            w.Host = this;
            _widgets.Add(w);
            var slot = MakeSlot(w.Tile, w.IsIcon);
            slot.Widget = w;
            WireWidget(slot, w);
            _items.Children.Add(slot);
            _itemIds.Add(id);
        }
        ApplyChrome();
        RefreshApps();
        Reposition();
        foreach (var w in _widgets) { try { w.Tick(); } catch { } }
    }

    Slot MakeSlot(FrameworkElement content, bool magnifies)
    {
        var s = Settings.Current;
        var slot = new Slot
        {
            Child = content,
            Magnifies = magnifies && s.Magnify,
            Margin = new Thickness(s.Spacing / 2.0, 0, s.Spacing / 2.0, 0),
            VerticalAlignment = AtTop ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            ClipToBounds = false,
        };
        slot.LayoutTransform = slot.ScaleT;
        slot.MouseEnter += (_, _) => SetHover(slot);
        slot.MouseLeave += (_, _) => { if (_hover == slot) SetHover(null); };
        slot.PreviewMouseLeftButtonDown += (_, e) => { _pressSlot = slot; _pressPoint = e.GetPosition(this); _dragged = false; };
        slot.PreviewMouseMove += (_, e) => MaybeStartDrag(slot, e);
        _slots.Add(slot);
        return slot;
    }

    void WireWidget(Slot slot, Widget w)
    {
        slot.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragged || _pressSlot != slot) return;
            _pressSlot = null;
            try { w.OnClick(); } catch (Exception ex) { App.Log(ex); }
            e.Handled = true;
        };
        slot.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { w.OnMiddleClick(); e.Handled = true; } };
        slot.MouseRightButtonUp += (_, e) =>
        {
            var menu = NewMenu();
            menu.Items.Add(Header(w.Name));
            w.AddMenuItems(menu);
            if (menu.Items.Count > 1) menu.Items.Add(new Separator());
            menu.Items.Add(Item("Remove from dock", "", () => Remove(w)));
            menu.Items.Add(Item("Widgets…", "", OpenWidgetPicker));
            OpenMenu(menu, slot);
            e.Handled = true;
        };
        slot.MouseWheel += (_, e) => { w.OnWheel(e.Delta); e.Handled = true; };
        if (w.AcceptsFiles)
        {
            slot.AllowDrop = true;
            slot.DragOver += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Move; e.Handled = true; } };
            slot.Drop += (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { w.Drop(files); e.Handled = true; }
            };
        }
    }

    /// <summary>Applies sizes, corner radius and fills to the bar.</summary>
    void ApplyChrome()
    {
        var s = Settings.Current;
        _bar.Height = BarHeight;
        _bar.CornerRadius = new CornerRadius(Math.Min(s.CornerRadius, BarHeight / 2));
        _bar.SetResourceReference(Border.BackgroundProperty, "Dock.Fill");
        if (s.Style == "clear") _bar.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        _bar.VerticalAlignment = AtTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _items.VerticalAlignment = AtTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _items.Margin = new Thickness(s.Padding + 2, s.Padding, s.Padding + 2, s.Padding);
        _items.HorizontalAlignment = HorizontalAlignment.Center;
        _dock.HorizontalAlignment = s.FullWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        _dock.VerticalAlignment = AtTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _dock.Margin = s.FullWidth
            ? new Thickness(s.EdgeGap, AtTop ? s.EdgeGap : 0, s.EdgeGap, AtTop ? 0 : s.EdgeGap)
            : new Thickness(0, AtTop ? s.EdgeGap : 0, 0, AtTop ? 0 : s.EdgeGap);
        _dock.MinWidth = BarHeight * 2;
        _glass.SetTint(Theme.GlassTint(s.Style));
    }

    // ───────────────────────── Position, app bar, glass ─────────────────────────

    public void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;
        var s = Settings.Current;
        _mon = Monitors.Pick(s.Monitor);
        _scale = _mon.Scale;
        bool top = AtTop;
        int winH = (int)Math.Ceiling((s.EdgeGap + BarHeight + Headroom) * _scale);
        int reserve = (int)Math.Ceiling((BarHeight + s.EdgeGap * 2) * _scale);

        var area = s.HideTaskbar ? _mon.Bounds : _mon.Work;
        int edge = top ? area.Top : area.Bottom;

        if (!s.AutoHide)
        {
            if (!_appBar) RegisterAppBar();
            var abd = AppBarData();
            abd.uEdge = top ? Native.ABE_TOP : Native.ABE_BOTTOM;
            abd.rc = new Native.RECT(_mon.Bounds.Left, top ? _mon.Bounds.Top : _mon.Bounds.Bottom - reserve, _mon.Bounds.Right, top ? _mon.Bounds.Top + reserve : _mon.Bounds.Bottom);
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
            if (top) abd.rc.Bottom = abd.rc.Top + reserve; else abd.rc.Top = abd.rc.Bottom - reserve;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
            edge = top ? abd.rc.Top : abd.rc.Bottom;
        }
        else if (_appBar) UnregisterAppBar();

        _windowRect = new Native.RECT(_mon.Bounds.Left, top ? edge : edge - winH, _mon.Bounds.Right, top ? edge + winH : edge);
        ApplyWindowPos();
    }

    void ApplyWindowPos()
    {
        var r = _windowRect;
        int travel = (int)((Settings.Current.EdgeGap + BarHeight + 4) * _scale);
        int dy = (int)Math.Round(_slide * travel) * (AtTop ? -1 : 1);
        Native.SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top + dy, r.Width, r.Height, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
        UpdateGlass();
    }

    void UpdateGlass()
    {
        if (_hwnd == IntPtr.Zero) return;
        var s = Settings.Current;
        if (!Theme.IsGlass(s.Style) || !IsVisible || _fullscreenHidden || _slide >= 0.99 || _bar.ActualWidth < 4) { _glass.HideGlass(); return; }
        try
        {
            var p = _bar.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Round(_bar.ActualWidth * dpi.DpiScaleX), h = (int)Math.Round(_bar.ActualHeight * dpi.DpiScaleY);
            _glass.Place(new Native.RECT((int)Math.Round(p.X), (int)Math.Round(p.Y), (int)Math.Round(p.X) + w, (int)Math.Round(p.Y) + h), (int)Math.Round(_bar.CornerRadius.TopLeft * dpi.DpiScaleX));
        }
        catch { }
    }

    Native.APPBARDATA AppBarData() => new() { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _appBarMsg };

    void RegisterAppBar()
    {
        var abd = AppBarData();
        Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
        _appBar = true;
    }

    void UnregisterAppBar()
    {
        if (!_appBar) return;
        var abd = AppBarData();
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
        _appBar = false;
    }

    bool _repositionQueued;
    void QueueReposition()
    {
        if (_repositionQueued) return;
        _repositionQueued = true;
        Dispatcher.BeginInvoke(() => { _repositionQueued = false; Reposition(); }, DispatcherPriority.Background);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _appBarMsg && _appBarMsg != 0)
        {
            if (wParam.ToInt32() == Native.ABN_POSCHANGED) QueueReposition();
        }
        else if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
        {
            // Explorer restarted: its taskbar is back and our app bar registration is gone.
            _appBar = false;
            Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(1500);
                if (Settings.Current.HideTaskbar) Taskbar.Hide();
                Reposition();
            });
        }
        else if (msg == 0x007E /* WM_DISPLAYCHANGE */ || msg == 0x02E0 /* WM_DPICHANGED */) QueueReposition();
        return IntPtr.Zero;
    }

    bool _shutDown;

    /// <summary>Releases the app bar and closes the helper windows. Safe to call more than once,
    /// including while the glass window (the dock's owner) is itself closing.</summary>
    public void Shutdown()
    {
        if (_shutDown) return;
        _shutDown = true;
        _poll.Stop();
        _second.Stop();
        UnregisterAppBar();
        try { _preview.Close(); } catch (InvalidOperationException) { }
        try { _glass.Close(); } catch (InvalidOperationException) { }
    }

    // ───────────────────────── Magnification and labels ─────────────────────────

    void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        CompositionTarget.Rendering += OnRender;
    }

    void OnRender(object? sender, EventArgs e)
    {
        var s = Settings.Current;
        bool moving = false;
        double range = s.IconSize * 2.6;
        foreach (var slot in _slots)
        {
            if (!slot.Magnifies || !slot.IsVisible) continue;
            double target = 1;
            if (!double.IsNaN(_mouseX) && _card == null && !_dragging)
            {
                double c = slot.TranslatePoint(new Point(slot.ActualWidth / 2, 0), _items).X;
                double d = Math.Abs(_mouseX - c);
                if (d < range) target = 1 + (s.MagnifyScale - 1) * (Math.Cos(d / range * Math.PI) + 1) / 2;
            }
            double next = slot.Current + (target - slot.Current) * 0.32;
            if (Math.Abs(next - target) < 0.004) next = target; else moving = true;
            if (next != slot.Current)
            {
                slot.Current = next;
                slot.ScaleT.ScaleX = slot.ScaleT.ScaleY = next;
            }
        }
        PlaceLabel();
        if (!moving && double.IsNaN(_mouseX))
        {
            CompositionTarget.Rendering -= OnRender;
            _rendering = false;
        }
    }

    void SetHover(Slot? slot)
    {
        _hover = slot;
        if (slot == null) { _label.Visibility = Visibility.Collapsed; CancelPreview(); return; }
        if (Settings.Current.ShowLabels && slot.ShowsLabel && slot.Label.Length > 0 && _card == null && !_preview.IsVisible)
        {
            _labelText.Text = slot.Label;
            _label.Visibility = Visibility.Visible;
            PlaceLabel();
        }
        else _label.Visibility = Visibility.Collapsed;

        if (slot.App?.Running == true) { _previewSlot = slot; _previewTimer.Stop(); _previewTimer.Start(); }
        else CancelPreview();
    }

    void PlaceLabel()
    {
        if (_hover == null || _label.Visibility != Visibility.Visible) return;
        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _label.DesiredSize;
        var p = _hover.TranslatePoint(new Point(_hover.ActualWidth / 2, AtTop ? _hover.ActualHeight : 0), _root);
        double scale = _hover.Current;
        // TranslatePoint already includes the layout transform, so the slot's top edge moves with magnification.
        Canvas.SetLeft(_label, Math.Clamp(p.X - size.Width / 2, 4, Math.Max(4, ActualWidth - size.Width - 4)));
        Canvas.SetTop(_label, AtTop ? p.Y + 8 : p.Y - size.Height - 8);
        _ = scale;
    }

    // ───────────────────────── Previews ─────────────────────────

    void CancelPreview()
    {
        _previewTimer.Stop();
        if (_preview.IsVisible) _previewHide.Start();
    }

    void ShowPreview()
    {
        var slot = _previewSlot;
        if (slot?.App?.Group is not { Windows.Count: > 0 } group || _card != null || _hover != slot) return;
        _label.Visibility = Visibility.Collapsed;
        var anchor = slot.PointToScreen(new Point(slot.ActualWidth / 2, AtTop ? slot.ActualHeight : 0));
        _preview.ShowFor(group, anchor, AtTop);
    }

    void CheckPreviewHide()
    {
        _previewHide.Stop();
        if (!_preview.IsVisible) return;
        if (_preview.PointerInside() || (_hover != null && _hover == _previewSlot)) return;
        HidePreview();
    }

    void HidePreview()
    {
        _previewTimer.Stop();
        _previewHide.Stop();
        if (_preview.IsVisible) _preview.Hide();
    }

    // ───────────────────────── Cards ─────────────────────────

    public void ToggleCard(Widget w)
    {
        if (_cardWidget == w && _card?.IsOpen == true) { CloseCard(); return; }
        if (_lastClosedCard == w && (DateTime.Now - _lastClosedAt).TotalMilliseconds < 150) return; // focus loss closed it a moment ago
        OpenCard(w);
    }

    public void OpenCard(Widget w)
    {
        CloseCard();
        HidePreview();
        FrameworkElement? content;
        w.CardOpen = true; // so the widget fills in its live values while building
        try { content = w.BuildCard(); } catch (Exception ex) { App.Log(ex); w.CardOpen = false; return; }
        if (content == null) { w.CardOpen = false; return; }
        var slot = _slots.FirstOrDefault(x => x.Widget == w);
        if (slot == null) return;

        _label.Visibility = Visibility.Collapsed;
        double cardW = w.CardWidth + 28; // card margin holds the shadow
        double center = slot.TranslatePoint(new Point(slot.ActualWidth / 2, 0), _bar).X;
        // StaysOpen is on so a click on another tile switches cards in one go; the card closes
        // when Perch loses focus, on Esc, or when another card opens.
        var popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = true,
            PlacementTarget = _bar,
            Placement = AtTop ? PlacementMode.Bottom : PlacementMode.Top,
            HorizontalOffset = center - cardW / 2,
            VerticalOffset = AtTop ? -8 : 8,
            PopupAnimation = PopupAnimation.Fade,
            Child = Ui.Card(content, w.CardWidth),
        };
        popup.Closed += (_, _) =>
        {
            if (_card != popup) return;
            _card = null;
            _cardWidget = null;
            w.CardOpen = false;
            _lastClosedCard = w;
            _lastClosedAt = DateTime.Now;
            try { w.OnCardClosed(); } catch { }
            StartRendering();
        };
        popup.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CloseCard(); e.Handled = true; } };
        _card = popup;
        _cardWidget = w;
        w.CardOpen = true;
        _mouseX = double.NaN;
        StartRendering();
        Activate();
        popup.IsOpen = true;
    }

    public void RefreshCard(Widget w)
    {
        if (_cardWidget != w || _card == null) return;
        try { if (w.BuildCard() is { } c) _card.Child = Ui.Card(c, w.CardWidth); } catch (Exception ex) { App.Log(ex); }
    }

    public void CloseCard()
    {
        if (_card != null) _card.IsOpen = false;
    }

    public void Remove(Widget w)
    {
        Settings.Update(s => s.Items.Remove(w.Id), rebuild: true);
    }

    public void Bounce(FrameworkElement e)
    {
        if (e.RenderTransform is not TranslateTransform t) { t = new TranslateTransform(); e.RenderTransform = t; }
        double h = Settings.Current.IconSize * 0.3 * (AtTop ? 1 : -1);
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(h, KeyTime.FromPercent(0.22), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.45), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(h * 0.5, KeyTime.FromPercent(0.65), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.85), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        t.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    // ───────────────────────── Polling: auto-hide and full screen ─────────────────────────

    void Poll()
    {
        var s = Settings.Current;
        _pollCount++;

        if (s.HideInFullscreen && _pollCount % 5 == 0)
        {
            bool fs = ForegroundIsFullscreen();
            if (fs != _fullscreenHidden)
            {
                _fullscreenHidden = fs;
                if (fs) { CloseCard(); HidePreview(); Native.ShowWindow(_hwnd, Native.SW_HIDE); _glass.HideGlass(); }
                else { Native.ShowWindow(_hwnd, Native.SW_SHOWNA); UpdateGlass(); }
            }
        }

        if (!s.AutoHide)
        {
            if (_slide != 0) { _slideTarget = 0; Slide(); }
            return;
        }

        Native.GetCursorPos(out var pt);
        bool busy = _card != null || _preview.IsVisible || _dragging || _menuOpen;
        bool insideX = pt.X >= _mon.Bounds.Left && pt.X < _mon.Bounds.Right;
        bool atEdge = insideX && (AtTop ? pt.Y <= _mon.Bounds.Top + 1 : pt.Y >= _mon.Bounds.Bottom - 2);
        bool overDock = PointerOverDock(pt);
        if (overDock || busy || Taskbar.ShellSurfaceOpen()) _lastInside = DateTime.Now;

        if (_slideTarget > 0 && (atEdge || Taskbar.ShellSurfaceOpen())) _slideTarget = 0;
        else if (_slideTarget == 0 && !busy && !overDock && (DateTime.Now - _lastInside).TotalMilliseconds > 700 && !ShowOnDesktop()) _slideTarget = 1;
        Slide();
    }

    bool ShowOnDesktop() => false;

    bool PointerOverDock(Native.POINT pt)
    {
        try
        {
            var p = _bar.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(this);
            double w = _bar.ActualWidth * dpi.DpiScaleX, h = _bar.ActualHeight * dpi.DpiScaleY;
            double extra = Headroom * dpi.DpiScaleY;
            double top = AtTop ? p.Y : p.Y - extra, bottom = AtTop ? p.Y + h + extra : p.Y + h + Settings.Current.EdgeGap * dpi.DpiScaleY + 2;
            if (AtTop) top -= Settings.Current.EdgeGap * dpi.DpiScaleY + 2;
            return pt.X >= p.X - 8 && pt.X <= p.X + w + 8 && pt.Y >= top && pt.Y <= bottom && _slide < 0.5;
        }
        catch { return false; }
    }

    void Slide()
    {
        if (Math.Abs(_slide - _slideTarget) < 0.001) return;
        double step = _slideTarget > _slide ? 0.22 : 0.34;
        _slide = _slideTarget > _slide ? Math.Min(_slideTarget, _slide + step) : Math.Max(_slideTarget, _slide - step);
        _autoHidden = _slide > 0.99;
        ApplyWindowPos();
        if (Math.Abs(_slide - _slideTarget) > 0.001)
            Dispatcher.BeginInvoke(Slide, DispatcherPriority.Render);
    }

    bool ForegroundIsFullscreen()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd) return false;
        Native.GetWindowThreadProcessId(fg, out int pid);
        if (pid == Environment.ProcessId) return false;
        var cls = Native.GetClass(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow") return false;
        if (Native.IsZoomed(fg) || !Native.IsWindowVisible(fg)) return false;
        if (!Native.GetWindowRect(fg, out var r)) return false;
        var b = _mon.Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }

    // ───────────────────────── Menus ─────────────────────────

    bool _menuOpen;

    static ContextMenu NewMenu() => new() { Placement = PlacementMode.MousePoint };

    static MenuItem Header(string text) => new() { Header = Ui.Text(text, 12.5, FontWeights.SemiBold, "Card.Dim"), IsEnabled = false };

    static MenuItem Item(string text, string glyph, Action click, bool? check = null)
    {
        var mi = new MenuItem { Header = text, Tag = glyph };
        if (check != null) { mi.IsCheckable = true; mi.IsChecked = check.Value; }
        mi.Click += (_, _) => click();
        return mi;
    }

    void OpenMenu(ContextMenu menu, UIElement target)
    {
        CloseCard();
        HidePreview();
        _label.Visibility = Visibility.Collapsed;
        menu.PlacementTarget = target;
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => _menuOpen = false;
        Activate();
        menu.IsOpen = true;
    }

    void ShowDockMenu()
    {
        var s = Settings.Current;
        var menu = NewMenu();
        menu.Items.Add(Item("Widgets…", "", OpenWidgetPicker));
        menu.Items.Add(Item("Settings…", "", OpenSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Lock dock", "", () => Settings.Update(x => x.Locked = !x.Locked), s.Locked));
        menu.Items.Add(Item("Auto-hide", "", () => Settings.Update(x => x.AutoHide = !x.AutoHide, rebuild: true), s.AutoHide));
        menu.Items.Add(Item("Hide the Windows taskbar", "", ToggleTaskbar, s.HideTaskbar));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Task Manager", "", () => Apps.Open("taskmgr.exe")));
        menu.Items.Add(Item("Quit Perch", "", () => Application.Current.Shutdown()));
        OpenMenu(menu, _bar);
    }

    void ToggleTaskbar()
    {
        bool hide = !Settings.Current.HideTaskbar;
        Settings.Update(x => x.HideTaskbar = hide, rebuild: true);
        if (hide) Taskbar.Hide(); else Taskbar.Restore();
        QueueReposition();
    }

    public void OpenSettings() => SettingsWindow.ShowSingle();
    public void OpenWidgetPicker() => WidgetPicker.ShowSingle();

    protected override void OnClosed(EventArgs e)
    {
        Shutdown();
        base.OnClosed(e);
        Application.Current.Shutdown(); // Alt+F4 on the dock quits Perch and brings the taskbar back
    }
}
