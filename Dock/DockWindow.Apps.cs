using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Dock;

public sealed partial class DockWindow
{
    readonly Dictionary<string, Slot> _appIcons = [];
    string _appSignature = "";
    bool _dragging;

    // ───────────────────────── Apps ─────────────────────────

    /// <summary>Pinned apps first (in pin order), then running apps that aren't pinned.</summary>
    bool _refreshingApps;

    void RefreshApps()
    {
        if (_apps == null) return;
        if (_refreshingApps) { Dispatcher.BeginInvoke(RefreshApps); return; } // never re-enter while children are changing
        _refreshingApps = true;
        try { RefreshAppsCore(); }
        finally { _refreshingApps = false; }
    }

    void RefreshAppsCore()
    {
        if (_apps == null) return;
        var s = Settings.Current;
        var groups = WindowTracker.Instance.Groups;
        var entries = new List<(string Key, PinnedApp? Pin, AppGroup? Group)>();
        var used = new HashSet<AppGroup>();
        foreach (var pin in s.Pins)
        {
            var g = groups.FirstOrDefault(x => !used.Contains(x) && Apps.Matches(pin, x));
            if (g != null) used.Add(g);
            entries.Add(("pin:" + pin.Launch.ToLowerInvariant() + "|" + pin.AppId, pin, g));
        }
        foreach (var g in groups.Where(g => !used.Contains(g))) entries.Add(("run:" + g.Key, null, g));

        if (_appsGroup != null) _appsGroup.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        string sig = string.Join("|", entries.Select(e => e.Key));
        var lastActive = WindowTracker.Instance.LastActive;
        if (sig != _appSignature)
        {
            _appSignature = sig;
            var keep = new HashSet<string>(entries.Select(e => e.Key));
            foreach (var k in _appIcons.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                _slots.Remove(_appIcons[k]);
                _appIcons.Remove(k);
            }
            _apps.Children.Clear();
            foreach (var e in entries)
            {
                if (!_appIcons.TryGetValue(e.Key, out var slot))
                {
                    var icon = new AppIcon(e.Key, s.IconSize, s.Padding);
                    slot = MakeSlot(icon, true);
                    slot.App = icon;
                    WireApp(slot, icon);
                    _appIcons[e.Key] = slot;
                }
                _apps.Children.Add(slot);
            }
        }
        foreach (var e in entries)
        {
            if (!_appIcons.TryGetValue(e.Key, out var s2) || s2.App is not { } icon) { _appSignature = ""; continue; }
            bool active = e.Group != null && e.Group.Windows.Any(w => w.Hwnd == lastActive) && !Native.IsIconic(lastActive);
            icon.Update(e.Pin, e.Group, active);
        }
    }

    void WireApp(Slot slot, AppIcon icon)
    {
        slot.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragged || _pressSlot != slot) return;
            _pressSlot = null;
            HidePreview();
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (icon.Running && !shift) WindowTracker.Instance.Toggle(icon.Group!);
            else LaunchNew(icon);
            e.Handled = true;
        };
        slot.MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            LaunchNew(icon);
            e.Handled = true;
        };
        slot.MouseRightButtonUp += (_, e) => { ShowAppMenu(slot, icon); e.Handled = true; };
        slot.AllowDrop = true;
        slot.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
        };
        slot.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            e.Handled = true;
            // Dropping files on an app opens them with it.
            string args = string.Join(" ", files.Select(f => $"\"{f}\""));
            var target = icon.Pin?.Launch ?? icon.Group?.ExePath;
            if (target != null && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) { Apps.Launch(target, args); Bounce(icon); }
        };
    }

    void LaunchNew(AppIcon icon)
    {
        if (icon.Pin != null) Apps.Launch(icon.Pin);
        else if (icon.Group != null) Apps.Launch(Apps.FromGroup(icon.Group));
        Bounce(icon);
    }

    void ShowAppMenu(Slot slot, AppIcon icon)
    {
        var menu = NewMenu();
        menu.Items.Add(Header(icon.Label));
        if (icon.Group != null)
        {
            foreach (var w in icon.Group.Windows.Take(10))
            {
                var title = w.Title.Length > 48 ? w.Title[..47] + "…" : w.Title;
                menu.Items.Add(Item(title, w.Minimized ? "" : "", () => WindowTracker.Activate(w.Hwnd)));
            }
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item(icon.Running ? "New window" : "Open", "", () => LaunchNew(icon)));
        if (icon.Pin != null)
        {
            var pin = icon.Pin;
            menu.Items.Add(Item("Unpin from dock", "", () => Apps.Unpin(pin)));
        }
        else if (icon.Group != null)
        {
            var g = icon.Group;
            menu.Items.Add(Item("Pin to dock", "", () => Apps.Pin(Apps.FromGroup(g))));
        }
        string? location = icon.Pin?.Target ?? icon.Group?.ExePath ?? (icon.Pin?.Launch.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) == true ? icon.Pin.Launch : null);
        if (location != null && File.Exists(location)) menu.Items.Add(Item("Open file location", "", () => Apps.OpenFileLocation(location)));
        if (icon.Group is { Windows.Count: > 0 } grp)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(grp.Windows.Count > 1 ? "Close all windows" : "Close window", "", () => { foreach (var w in grp.Windows) WindowTracker.Close(w.Hwnd); }));
            menu.Items.Add(Item("End task", "", () => WindowTracker.EndTask(grp)));
        }
        OpenMenu(menu, slot);
    }

    // ───────────────────────── Drag and drop ─────────────────────────

    void MaybeStartDrag(Slot slot, MouseEventArgs e)
    {
        if (_pressSlot != slot || e.LeftButton != MouseButtonState.Pressed || _dragging) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _pressPoint.X) < 8 && Math.Abs(p.Y - _pressPoint.Y) < 8) return;
        if (Settings.Current.Locked)
        {
            // Say why nothing moves, instead of silently ignoring the drag.
            _dragged = true; // so releasing doesn't count as a click
            _hover = slot;
            _labelText.Text = "The dock is locked. Right-click the dock and untick Lock dock to move things.";
            _label.Visibility = Visibility.Visible;
            PlaceLabel();
            return;
        }
        if (slot.App != null && slot.App.Pin == null && slot.App.Group == null) return;
        _dragged = true;
        _dragging = true;
        HidePreview();
        CloseCard();
        _label.Visibility = Visibility.Collapsed;
        slot.Opacity = 0.45;
        slot.ReleaseMouseCapture(); // drag and drop takes over the pointer from here
        _root.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // catch drops anywhere on the strip
        try { DragDrop.DoDragDrop(slot, new DataObject("perch/slot", slot), DragDropEffects.Move); }
        finally
        {
            _root.Background = null;
            slot.Opacity = 1;
            _dragging = false;
            _pressSlot = null;
        }
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("perch/slot")) e.Effects = DragDropEffects.Move;
        else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Link;
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData("perch/slot") is Slot slot)
        {
            if (slot.App != null) DropApp(slot, e);
            else DropWidget(slot, e);
            return;
        }
        // Files, folders and shortcuts dropped on the dock get pinned.
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files)
                if (Apps.FromFile(f) is { } pin) Apps.Pin(pin);
        }
    }

    void DropWidget(Slot slot, DragEventArgs e)
    {
        int from = _items.Children.IndexOf(slot);
        if (from < 0) return;
        double x = e.GetPosition(_items).X;
        int to = 0;
        foreach (UIElement child in _items.Children)
        {
            if (child == slot) continue;
            var fe = (FrameworkElement)child;
            double c = fe.TranslatePoint(new Point(fe.ActualWidth / 2, 0), _items).X;
            if (c < x) to++;
        }
        var ids = _itemIds.ToList();
        var id = ids[from];
        ids.RemoveAt(from);
        ids.Insert(Math.Clamp(to, 0, ids.Count), id);
        Settings.Update(s => s.Items = ids, rebuild: true);
    }

    void DropApp(Slot slot, DragEventArgs e)
    {
        if (_apps == null) return;
        var icon = slot.App!;
        double x = e.GetPosition(_apps).X;
        var pins = Settings.Current.Pins;
        var pinSlots = _apps.Children.OfType<Slot>().Where(s => s.App?.Pin != null && s != slot).ToList();
        int to = pinSlots.Count(s => s.TranslatePoint(new Point(s.ActualWidth / 2, 0), _apps).X < x);
        var pin = icon.Pin ?? (icon.Group != null ? Apps.FromGroup(icon.Group) : null);
        if (pin == null) return;
        Settings.Update(s =>
        {
            s.Pins.Remove(pin);
            s.Pins.Insert(Math.Clamp(to, 0, s.Pins.Count), pin);
        });
        _appSignature = "";
        RefreshApps();
        _ = pins;
    }
}
