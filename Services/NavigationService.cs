using AudioBookshelfApp.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace AudioBookshelfApp.Services;

public class NavigationService : INavigationService
{
    private Frame? _frame;
    private NavigationView? _navView;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void Initialize(Frame frame, NavigationView navView)
    {
        _frame = frame;
        _navView = navView;
    }

    public void NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame == null) return;
        if (_frame.CurrentSourcePageType == pageType) return;

        _frame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromRight
        });
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }

    public void GoHome()
    {
        if (_frame == null) return;
        NavigateTo(typeof(HomePage));

        // Sync nav selection
        if (_navView?.MenuItems.Count > 0)
            _navView.SelectedItem = _navView.MenuItems[0];
    }
}
