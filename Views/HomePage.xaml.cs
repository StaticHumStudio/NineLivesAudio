using NineLivesAudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NineLivesAudio.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    // Computed visibility: show grid when not loading and not empty
    public bool ShowGrid => !ViewModel.IsLoading && !ViewModel.ShowEmptyState;

    // Touch scroll vs tap discrimination
    private Windows.Foundation.Point? _pointerPressedPosition;
    private object? _pointerPressedTag;
    private const double TapDistanceThreshold = 12.0;

    // Hover brush cache
    private static readonly SolidColorBrush HoverBrush =
        new(Windows.UI.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)); // subtle white overlay

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        this.InitializeComponent();
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;

        // Load the logo from app assets
        LoadLogo();

        // Update ShowGrid when relevant properties change
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.IsLoading) or nameof(ViewModel.ShowEmptyState))
        {
            Bindings.Update();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        this.Loaded -= OnLoaded;
        this.Unloaded -= OnUnloaded;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        if (ViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void LoadLogo()
    {
        try
        {
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "nine-lives-logo.png");
            if (System.IO.File.Exists(logoPath))
            {
                LogoImage.Source = new BitmapImage(new Uri(logoPath));
            }
        }
        catch
        {
            // Logo load failure is non-fatal
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            var isMostRecent = (grid.Tag as NineLivesItem)?.IsMostRecent ?? false;
            if (!isMostRecent)
            {
                grid.Background = HoverBrush;
            }
        }
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            var item = grid.Tag as NineLivesItem;
            var isMostRecent = item?.IsMostRecent ?? false;
            if (isMostRecent)
            {
                // Restore VoidSurface highlight
                grid.Background = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0x11, 0x18, 0x27));
            }
            else
            {
                grid.Background = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00));
            }
        }
    }

    private void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.Tag is NineLivesItem item)
        {
            var point = e.GetCurrentPoint(grid);
            if (point.Properties.IsLeftButtonPressed)
            {
                _pointerPressedPosition = point.Position;
                _pointerPressedTag = item;
            }
        }
    }

    private void Card_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerPressedPosition == null) return;
        if (sender is Grid grid)
        {
            var point = e.GetCurrentPoint(grid);
            var dx = point.Position.X - _pointerPressedPosition.Value.X;
            var dy = point.Position.Y - _pointerPressedPosition.Value.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > TapDistanceThreshold)
            {
                _pointerPressedPosition = null;
                _pointerPressedTag = null;
            }
        }
    }

    private void Card_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerPressedPosition != null && _pointerPressedTag is NineLivesItem item)
        {
            Frame.Navigate(typeof(BookDetailPage), item.AudioBook);
        }
        _pointerPressedPosition = null;
        _pointerPressedTag = null;
    }

    private void GoToLibrary_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to Library page via the main window's NavigationView
        if (this.XamlRoot?.Content is Grid rootGrid)
        {
            // Walk up to MainWindow's ContentFrame and navigate
            var frame = this.Frame;
            frame?.Navigate(typeof(LibraryPage));
        }
    }
}
