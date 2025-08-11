using System;
using System.Threading;

namespace StrmAssistant.Common
{
    public static class ConditionalLock
    {
        private static readonly object SqliteQueryLock = new object();
        private static int _counter = 0;
        private static readonly ThreadLocal<bool> InGate = new ThreadLocal<bool>(() => false);
        
        public static T Run<T>(Func<T> func)
        {
            T result = default!;
            DoWork(() => result = func());
            return result;
        }

        private static void DoWork(Action action)
        {
            if (InGate.Value)
            {
                action();
                return;
            }

            if (Interlocked.Increment(ref _counter) == 1)
            {
                InGate.Value = true;
                try
                {
                    action();
                }
                finally
                {
                    InGate.Value = false;
                    Interlocked.Decrement(ref _counter);
                }
            }
            else
            {
                try
                {
                    Monitor.Enter(SqliteQueryLock);
                    InGate.Value = true;
                    action();
                }
                finally
                {
                    InGate.Value = false;
                    Monitor.Exit(SqliteQueryLock);
                    Interlocked.Decrement(ref _counter);
                }
            }
        }
    }
}
