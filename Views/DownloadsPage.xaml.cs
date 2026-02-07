using System.ComponentModel;
using System.Collections.Specialized;
using AudioBookshelfApp.Models;
using AudioBookshelfApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AudioBookshelfApp.Views;

public sealed partial class DownloadsPage : Page, INotifyPropertyChanged
{
    public DownloadsViewModel ViewModel { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasActiveDownloads => ViewModel.ActiveDownloads.Any();
    public bool HasCompletedDownloads => ViewModel.CompletedDownloads.Any();
    public bool ShowEmptyState => !ViewModel.IsLoading && !HasActiveDownloads && !HasCompletedDownloads;

    public DownloadsPage()
    {
        ViewModel = App.Services.GetRequiredService<DownloadsViewModel>();
        this.InitializeComponent();

        // Subscribe to collection changes so visibility bindings update
        ViewModel.ActiveDownloads.CollectionChanged += OnCollectionChanged;
        ViewModel.CompletedDownloads.CollectionChanged += OnCollectionChanged;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        NotifyVisibilityChanged();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(NotifyVisibilityChanged);
    }

    private void NotifyVisibilityChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveDownloads)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCompletedDownloads)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowEmptyState)));
    }

    private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DownloadItem download)
        {
            if (download.Status == DownloadStatus.Downloading)
            {
                _ = ViewModel.PauseDownloadCommand.ExecuteAsync(download);
            }
            else if (download.Status == DownloadStatus.Paused)
            {
                _ = ViewModel.ResumeDownloadCommand.ExecuteAsync(download);
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DownloadItem download)
        {
            _ = ViewModel.CancelDownloadCommand.ExecuteAsync(download);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DownloadItem download)
        {
            _ = ViewModel.DeleteDownloadCommand.ExecuteAsync(download);
        }
    }

    private void GoToLibrary_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(LibraryPage));
    }
}
