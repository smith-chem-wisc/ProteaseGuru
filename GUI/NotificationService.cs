using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace GUI
{
    /// <summary>
    /// Service for managing application-wide notifications.
    /// Replaces MessageBox popups with expandable notification panel.
    /// </summary>
    public class NotificationService : INotifyPropertyChanged
    {
        private static NotificationService? _instance;
        private static readonly object _lock = new object();
        private bool _isExpanded;

        private NotificationService()
        {
            Notifications = new ObservableCollection<NotificationMessage>();
        }

        public static NotificationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new NotificationService();
                        }
                    }
                }
                return _instance;
            }
        }

        public ObservableCollection<NotificationMessage> Notifications { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Adds a new notification and expands the notification panel.
        /// Only adds the notification if it's unique (not already in the list).
        /// </summary>
        public void AddNotification(string message, NotificationType type = NotificationType.Information)
        {
            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                AddNotificationInternal(message, type);
            }
            else
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => AddNotificationInternal(message, type)));
            }
        }

        private void AddNotificationInternal(string message, NotificationType type)
        {
            // Check if an identical notification already exists
            bool isDuplicate = Notifications.Any(n => 
                n.Message == message && 
                n.Type == type);

            // Only add if it's not a duplicate
            if (!isDuplicate)
            {
                var notification = new NotificationMessage
                {
                    Message = message,
                    Type = type,
                    Timestamp = DateTime.Now
                };

                Notifications.Insert(0, notification); // Add to top of list
                IsExpanded = true; // Auto-expand when new notification arrives

                // Keep only the last 100 notifications to avoid memory issues
                while (Notifications.Count > 100)
                {
                    Notifications.RemoveAt(Notifications.Count - 1);
                }
            }
        }

        /// <summary>
        /// Clears all notifications.
        /// </summary>
        public void ClearNotifications()
        {
            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                Notifications.Clear();
            }
            else
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => Notifications.Clear()));
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents a single notification message.
    /// </summary>
    public class NotificationMessage
    {
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; }
        public DateTime Timestamp { get; set; }

        public string TypeIcon
        {
            get
            {
                return Type switch
                {
                    NotificationType.Information => "ℹ️",
                    NotificationType.Warning => "⚠️",
                    NotificationType.Error => "❌",
                    NotificationType.Success => "✔️",
                    _ => "ℹ️"
                };
            }
        }

        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss");
    }

    /// <summary>
    /// Types of notification messages.
    /// </summary>
    public enum NotificationType
    {
        Information,
        Warning,
        Error,
        Success
    }
}
