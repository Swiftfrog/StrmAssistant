using StrmAssistant.Mod;
using System;

namespace StrmAssistant.Reflection
{
    internal abstract class ReflectionBase<T> where T : ReflectionBase<T>
    {
        internal PatchTracker PatchTracker = new PatchTracker(typeof(T), PatchApproach.Reflection);

        protected void Initialize()
        {
            try
            {
                OnInitialize();
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Debug(e.Message);
                    Plugin.Instance.Logger.Debug(e.StackTrace);
                }

                Plugin.Instance.Logger.Warn($"{PatchTracker.PatchType.Name} Init Failed");
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
            }
        }

        protected abstract void OnInitialize();
    }
}
