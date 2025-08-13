using Emby.Notifications;
using HarmonyLib;
using System.Reflection;

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
            _convertToGroups = AccessTools.Method(typeof(NotificationManager), "ConvertToGroups");
            _sendNotification = AccessTools.Method(typeof(NotificationManager), "SendNotification",
                new[] { typeof(INotifier), typeof(NotificationInfo[]), typeof(NotificationRequest), typeof(bool) });
            _queueNotification = AccessTools.Method(typeof(NotificationQueueManager), "QueueNotification",
                new[] { typeof(INotifier), typeof(InternalNotificationRequest), typeof(int) });
        }
    }
}
