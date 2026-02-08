using NineLivesAudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NineLivesAudio.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SolidColorBrush ConnectionStatusColor => ViewModel.IsConnected
        ? (SolidColorBrush)Application.Current.Resources["RitualSuccessBrush"]
        : (SolidColorBrush)Application.Current.Resources["MistFaintBrush"];

    public int SpeedSelectedIndex
    {
        get => ViewModel.PlaybackSpeed switch
        {
            0.5 => 0,
            0.75 => 1,
            1.0 => 2,
            1.25 => 3,
            1.5 => 4,
            1.75 => 5,
            2.0 => 6,
            _ => 2
        };
        set => ViewModel.PlaybackSpeed = value switch
        {
            0 => 0.5,
            1 => 0.75,
            2 => 1.0,
            3 => 1.25,
            4 => 1.5,
            5 => 1.75,
            6 => 2.0,
            _ => 1.0
        };
    }

    public int SyncIntervalSelectedIndex
    {
        get => ViewModel.SyncIntervalMinutes switch
        {
            1 => 0,
            5 => 1,
            10 => 2,
            15 => 3,
            30 => 4,
            _ => 1
        };
        set => ViewModel.SyncIntervalMinutes = value switch
        {
            0 => 1,
            1 => 5,
            2 => 10,
            3 => 15,
            4 => 30,
            _ => 5
        };
    }

    public int ThemeSelectedIndex
    {
        get => ViewModel.Theme switch
        {
            "System" => 0,
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        set => ViewModel.Theme = value switch
        {
            0 => "System",
            1 => "Light",
            2 => "Dark",
            _ => "System"
        };
    }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void ErrorInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.DismissErrorCommand.Execute(null);
    }

    private void SuccessInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.DismissSuccessCommand.Execute(null);
    }
}
