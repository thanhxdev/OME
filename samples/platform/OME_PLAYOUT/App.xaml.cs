using System;
using System.IO;
using System.Windows;

namespace OME_PLAYOUT
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                File.AppendAllText("crash_log.txt", $"[AppDomain Unhandled] {args.ExceptionObject}\n");
            };
            DispatcherUnhandledException += (s, args) =>
            {
                File.AppendAllText("crash_log.txt", $"[Dispatcher Unhandled] {args.Exception}\n");
            };
            base.OnStartup(e);
        }
    }
}
