using AudioBookshelfApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AudioBookshelfApp.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    // Computed visibility: show grid when not loading and not empty
    public bool ShowGrid => !ViewModel.IsLoading && !ViewModel.ShowEmptyState;

    // Touch scroll vs tap discrimination
    private Windows.Foundation.Point? _pointerPressedPosition;
    private object? _pointerPressedTag;
    private const double TapDistanceThreshold = 12.0;

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        this.InitializeComponent();
        this.Loaded += OnLoaded;

        // Update ShowGrid when relevant properties change
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.IsLoading) or nameof(ViewModel.ShowEmptyState))
            {
                Bindings.Update();
            }
        };

        // When Lives collection changes (e.g. after sync reload), refresh cover images
        ViewModel.Lives.CollectionChanged += (s, e) =>
        {
            // Small delay to let ItemsRepeater realize elements
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, LoadCoverImages);
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        LoadCoverImages();
    }

    private void LoadCoverImages()
    {
        // Load cover images for each item in the grid
        for (int i = 0; i < ViewModel.Lives.Count; i++)
        {
            var item = ViewModel.Lives[i];
            var coverPath = item.AudioBook.CoverPath;
            if (string.IsNullOrEmpty(coverPath)) continue;

            try
            {
                var element = LivesGrid.TryGetElement(i);
                if (element is Grid grid)
                {
                    // Find the Image element inside the cover art grid
                    var coverGrid = grid.Children[0] as Grid;
                    if (coverGrid == null) continue;

                    foreach (var child in coverGrid.Children)
                    {
                        if (child is Image img)
                        {
                            img.Source = new BitmapImage(new Uri(coverPath));
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Cover load failure is non-fatal
            }
        }
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Scale = new System.Numerics.Vector3(1.03f, 1.03f, 1f);
        }
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Scale = new System.Numerics.Vector3(1f, 1f, 1f);
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
