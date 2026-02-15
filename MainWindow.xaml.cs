using local_translate_provider.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.Resources;
using Windows.Graphics;

namespace local_translate_provider;

public sealed partial class MainWindow : Window
{
    private const double CollapseThreshold = 830;
    private static readonly ResourceLoader ResLoader = ResourceLoader.GetForViewIndependentUse();
    private readonly Action? _onClosing;

    public MainWindow(Action? onClosing = null)
    {
        _onClosing = onClosing;
        InitializeComponent();
        Title = ResLoader.GetString("WindowTitle");
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1700, 1200));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 350;
            presenter.PreferredMinimumHeight = 320;
        }
        NavView.SelectedItem = NavView.MenuItems[0];
        UpdatePageTitle("General");
        NavigateTo("General");
        UpdatePaneDisplayMode();
        AppWindow.Closing += OnAppWindowClosing;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // 清空导航栈和内容，断开对页面的引用，便于 GC 回收
        ContentFrame.BackStack.Clear();
        ContentFrame.Content = null;
        // 解除事件订阅，避免闭包持有窗口引用
        AppWindow.Closing -= OnAppWindowClosing;
        NavView.SelectionChanged -= NavView_SelectionChanged;
        NavView.Loaded -= NavView_Loaded;
        NavView.SizeChanged -= NavView_SizeChanged;
        _onClosing?.Invoke();
        // 不取消关闭，允许窗口销毁，使应用回归仅托盘运行的开销
    }

    private string _currentTag = "General";

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePaneDisplayMode();
    }

    private void NavView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePaneDisplayMode();
    }

    private void UpdatePaneDisplayMode()
    {
        var width = NavView.ActualWidth;
        var isMinimal = width < CollapseThreshold;
        NavView.PaneDisplayMode = isMinimal
            ? NavigationViewPaneDisplayMode.LeftMinimal
            : NavigationViewPaneDisplayMode.Left;
        // 折叠时左侧留出汉堡按钮宽度，避免标题重叠
        ContentGrid.Margin = isMinimal ? new Thickness(48, 0, 0, 0) : new Thickness(0, 0, 0, 0);
    }

    private void UpdatePageTitle(string tag)
    {
        var key = tag switch
        {
            "General" => "GeneralTitle/Text",
            "Model" => "ModelTitle/Text",
            "Service" => "ServiceTitle/Text",
            "About" => "AboutTitle/Text",
            _ => "GeneralTitle/Text"
        };
        PageTitle.Text = ResLoader.GetString(key);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag && tag != _currentTag)
        {
            _currentTag = tag;
            UpdatePageTitle(tag);
            _ = SaveCurrentAndNavigateAsync(tag);
        }
    }

    private async System.Threading.Tasks.Task SaveCurrentAndNavigateAsync(string newTag)
    {
        if (ContentFrame.Content is SettingsPage page)
            await page.SaveBeforeLeaveAsync();
        NavigateTo(newTag);
    }

    private void NavigateTo(string tag)
    {
        var transition = new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromBottom
        };
        ContentFrame.Navigate(typeof(SettingsPage), tag, transition);
    }
}
