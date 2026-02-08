namespace NineLivesAudio.Services;

public class NotificationService : INotificationService
{
    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    public void ShowSuccess(string message, string? title = null)
    {
        NotificationRequested?.Invoke(this, new NotificationEventArgs
        {
            Message = message,
            Title = title ?? "Success",
            Type = NotificationType.Success
        });
    }

    public void ShowError(string message, string? title = null)
    {
        NotificationRequested?.Invoke(this, new NotificationEventArgs
        {
            Message = message,
            Title = title ?? "Error",
            Type = NotificationType.Error
        });
    }

    public void ShowWarning(string message, string? title = null)
    {
        NotificationRequested?.Invoke(this, new NotificationEventArgs
        {
            Message = message,
            Title = title ?? "Warning",
            Type = NotificationType.Warning
        });
    }

    public void ShowInfo(string message, string? title = null)
    {
        NotificationRequested?.Invoke(this, new NotificationEventArgs
        {
            Message = message,
            Title = title,
            Type = NotificationType.Info
        });
    }

    public void Dismiss()
    {
        NotificationRequested?.Invoke(this, new NotificationEventArgs { ShouldDismiss = true });
    }
}
