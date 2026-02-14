using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace local_translate_provider.TrayIcon;

/// <summary>
/// Manages the system tray icon, double-click to show settings, and context menu.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly Action _onDoubleClick;
    private readonly Action _onExit;

    public TrayIconManager(Action onDoubleClick, Action onExit)
    {
        _onDoubleClick = onDoubleClick;
        _onExit = onExit;

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Local Translate Provider",
            DoubleClickCommand = new RelayCommand(_ => _onDoubleClick()),
            ContextFlyout = CreateContextMenu(),
            IconSource = new GeneratedIconSource { Text = "译", Size = 16 }
        };

        _taskbarIcon.ForceCreate();
    }

    private MenuFlyout CreateContextMenu()
    {
        var menu = new MenuFlyout();
        var showItem = new MenuFlyoutItem { Text = "显示设置" };
        showItem.Click += (_, _) => _onDoubleClick();
        menu.Items.Add(showItem);

        var exitItem = new MenuFlyoutItem { Text = "退出" };
        exitItem.Click += (_, _) => _onExit();
        menu.Items.Add(exitItem);

        return menu;
    }

    public void Dispose() => _taskbarIcon.Dispose();

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;

        public RelayCommand(Action<object?> execute) => _execute = execute;

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged;
    }
}
