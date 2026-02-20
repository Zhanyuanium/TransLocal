using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Resources;

namespace TransLocal.TrayIcon;

/// <summary>
/// Manages the system tray icon, double-click to show settings, and context menu.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private static readonly ResourceLoader ResLoader = ResourceLoader.GetForViewIndependentUse();

    private readonly TaskbarIcon _taskbarIcon;
    private readonly Action _onDoubleClick;
    private readonly Action _onExit;

    public TrayIconManager(Action onDoubleClick, Action onExit)
    {
        _onDoubleClick = onDoubleClick;
        _onExit = onExit;

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = ResLoader.GetString("TrayToolTip"),
            DoubleClickCommand = new RelayCommand(_ => _onDoubleClick()),
            ContextFlyout = CreateContextMenu(),
            IconSource = new GeneratedIconSource { Text = "译", Size = 16 }
        };

        _taskbarIcon.ForceCreate();
    }

    private MenuFlyout CreateContextMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = ResLoader.GetString("TrayShowSettings"),
            Command = new RelayCommand(_ => _onDoubleClick())
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = ResLoader.GetString("TrayExit"),
            Command = new RelayCommand(_ => _onExit())
        });
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
