using System.Windows;

namespace SRT_DECODE
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
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
                // Prevent crash dialog during shutdown
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                args.SetObserved();
            };
        }
    }
}
