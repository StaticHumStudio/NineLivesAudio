namespace AudioBookshelfApp.Services;

public interface IConnectivityService
{
    bool IsOnline { get; }
    bool IsServerReachable { get; }
    event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;
    Task StartMonitoringAsync();
    Task StopMonitoringAsync();
    Task<bool> CheckServerReachableAsync();
}

public class ConnectivityChangedEventArgs : EventArgs
{
    public bool IsOnline { get; set; }
    public bool IsServerReachable { get; set; }
}
