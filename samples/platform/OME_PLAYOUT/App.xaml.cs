using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace OME_PLAYOUT
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    File.AppendAllText("crash_log.txt", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [AppDomain Unhandled] {args.ExceptionObject}\n");
                }
                catch { }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    File.AppendAllText("crash_log.txt", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Dispatcher Unhandled] {args.Exception}\n");
                }
                catch { }
                args.Handled = true; // Prevent app from terminating on non-fatal UI exceptions
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                try
                {
                    File.AppendAllText("crash_log.txt", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [TaskScheduler Unobserved] {args.Exception}\n");
                }
                catch { }
                args.SetObserved(); // Mark as handled to prevent runtime shutdown
            };

            base.OnStartup(e);
        }
    }
}
