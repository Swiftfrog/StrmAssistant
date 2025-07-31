using Emby.Notifications;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class EmbyNotifications : ReflectionBase<EmbyNotifications>
    {
        internal static MethodInfo _convertToGroups;
        internal static MethodInfo _sendNotification;
        internal static MethodInfo _queueNotification;

        static EmbyNotifications()
        {
            new EmbyNotifications().Initialize();
        }

        protected override void OnInitialize()
        {
            var notificationsAssembly = GetAssemblyByName("Emby.Notifications");

            var notificationManager = notificationsAssembly.GetType("Emby.Notifications.NotificationManager");
            _convertToGroups = notificationManager.GetMethod("ConvertToGroups",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _sendNotification = notificationManager.GetMethod("SendNotification",
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(INotifier), typeof(NotificationInfo[]), typeof(NotificationRequest), typeof(bool) },
                null);
            var notificationQueueManager = notificationsAssembly.GetType("Emby.Notifications.NotificationQueueManager");
            _queueNotification = notificationQueueManager.GetMethod("QueueNotification",
                BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(INotifier), typeof(InternalNotificationRequest), typeof(int) }, null);
        }
    }
}
