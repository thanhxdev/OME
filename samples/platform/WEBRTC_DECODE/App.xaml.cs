using System.Windows;

namespace WEBRTC_DECODE
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    System.IO.File.AppendAllText("crash_log.txt", $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] [DispatcherUnhandledException] {args.Exception}\r\n");
                }
                catch { }

                if (args.Exception is System.OperationCanceledException ||
                    args.Exception is System.ObjectDisposedException ||
                    (args.Exception is System.InvalidOperationException invEx && invEx.Message.Contains("Dispatcher")))
                {
                    args.Handled = true;
                    return;
                }
                // Suppress popup during shutdown if window is closing
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    System.IO.File.AppendAllText("crash_log.txt", $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] [AppDomain.UnhandledException] {args.ExceptionObject}\r\n");
                }
                catch { }
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                try
                {
                    System.IO.File.AppendAllText("crash_log.txt", $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] [UnobservedTaskException] {args.Exception}\r\n");
                }
                catch { }
                args.SetObserved();
            };
        }
    }
}
