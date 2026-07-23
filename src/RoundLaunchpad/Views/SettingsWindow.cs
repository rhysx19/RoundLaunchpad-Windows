using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RoundLaunchpad.Models;
using RoundLaunchpad.Services;

namespace RoundLaunchpad.Views;

public class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly StackPanel _listPanel;
    private readonly CheckBox _doubleTap;
    private readonly CheckBox _openAtMouse;
    private readonly CheckBox _launchAtLogin;
    private readonly TextBlock _axHint;

    public SettingsWindow(SettingsStore store)
    {
        _store = store;
        Title = "RoundLaunchpad";
        Width = 460;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7));

        var root = new DockPanel { Margin = new Thickness(20) };

        var title = new TextBlock
        {
            Text = "Apps in the ring",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var addRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var addBtn = new Button { Content = "Add Apps…", Padding = new Thickness(12, 6, 12, 6) };
        addBtn.Click += (_, _) => AddApps();
        DockPanel.SetDock(addBtn, Dock.Left);
        addRow.Children.Add(addBtn);
        addRow.Children.Add(new TextBlock
        {
            Text = "Use ↑ ↓ to reorder",
            FontSize = 11,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        bottom.Children.Add(addRow);
        bottom.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 12) });

        _doubleTap = new CheckBox
        {
            Content = "Double-tap Alt opens the ring",
            IsChecked = _store.DoubleTapAlt,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _doubleTap.Checked += (_, _) => _store.DoubleTapAlt = true;
        _doubleTap.Unchecked += (_, _) => _store.DoubleTapAlt = false;
        bottom.Children.Add(_doubleTap);

        _axHint = new TextBlock
        {
            Text = "Uses a low-level keyboard hook. Some antivirus software may prompt; allow RoundLaunchpad if asked.",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC2, 0x7A, 0x00)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = _store.DoubleTapAlt ? Visibility.Visible : Visibility.Collapsed
        };
        _doubleTap.Checked += (_, _) => _axHint.Visibility = Visibility.Visible;
        _doubleTap.Unchecked += (_, _) => _axHint.Visibility = Visibility.Collapsed;
        bottom.Children.Add(_axHint);

        _openAtMouse = new CheckBox
        {
            Content = "Open the ring at the mouse pointer",
            IsChecked = _store.OpenAtMouse,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _openAtMouse.Checked += (_, _) => _store.OpenAtMouse = true;
        _openAtMouse.Unchecked += (_, _) => _store.OpenAtMouse = false;
        bottom.Children.Add(_openAtMouse);

        _launchAtLogin = new CheckBox
        {
            Content = "Launch at login",
            IsChecked = _store.LaunchAtLogin,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _launchAtLogin.Checked += (_, _) => _store.LaunchAtLogin = true;
        _launchAtLogin.Unchecked += (_, _) => _store.LaunchAtLogin = false;
        bottom.Children.Add(_launchAtLogin);

        bottom.Children.Add(new TextBlock
        {
            Text = "Alt+Space always toggles the ring (falls back to Ctrl+Alt+Space if taken). Esc or a click outside closes it. Hold Alt+Space, hover an app, release to launch.",
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(bottom);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD5)),
            Background = Brushes.White,
            Padding = new Thickness(4)
        };
        _listPanel = new StackPanel();
        scroll.Content = _listPanel;
        root.Children.Add(scroll);

        Content = root;
        ReloadList();
        _store.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsStore.Apps))
                Dispatcher.Invoke(ReloadList);
        };
    }

    private void ReloadList()
    {
        _listPanel.Children.Clear();
        var apps = _store.Apps.ToList();
        for (var i = 0; i < apps.Count; i++)
        {
            var app = apps[i];
            var index = i;
            var row = new DockPanel { Margin = new Thickness(6, 4, 6, 4) };

            var remove = new Button { Content = "−", Width = 28, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(remove, Dock.Right);
            remove.Click += (_, _) =>
            {
                _store.Remove(app);
                ReloadList();
            };
            row.Children.Add(remove);

            var down = new Button { Content = "↓", Width = 28, Margin = new Thickness(4, 0, 0, 0) };
            DockPanel.SetDock(down, Dock.Right);
            down.IsEnabled = index < apps.Count - 1;
            down.Click += (_, _) =>
            {
                _store.Move(index, index + 1);
                ReloadList();
            };
            row.Children.Add(down);

            var up = new Button { Content = "↑", Width = 28, Margin = new Thickness(4, 0, 0, 0) };
            DockPanel.SetDock(up, Dock.Right);
            up.IsEnabled = index > 0;
            up.Click += (_, _) =>
            {
                _store.Move(index, index - 1);
                ReloadList();
            };
            row.Children.Add(up);

            var img = new Image
            {
                Source = IconCache.GetIcon(app.Path, 32),
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(img);
            row.Children.Add(new TextBlock
            {
                Text = ShortcutResolver.DisplayName(app.Path),
                VerticalAlignment = VerticalAlignment.Center
            });

            _listPanel.Children.Add(row);
        }

        if (apps.Count == 0)
        {
            _listPanel.Children.Add(new TextBlock
            {
                Text = "No apps yet — click Add Apps…",
                Foreground = Brushes.Gray,
                Margin = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }

    private void AddApps()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose Applications",
            Filter = "Programs (*.exe;*.lnk)|*.exe;*.lnk|Executable (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        var start = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        if (Directory.Exists(start))
            dlg.InitialDirectory = start;

        if (dlg.ShowDialog(this) == true)
        {
            _store.AddPaths(dlg.FileNames);
            ReloadList();
        }
    }
}
