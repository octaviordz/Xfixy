using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;

namespace Xfixy.WinUI
{
    // https://stackoverflow.com/questions/74159985/uri-start-of-maui-windows-app-creates-a-new-instance-i-need-to-have-only-one-in
    // https://github.com/microsoft/WindowsAppSDK-Samples/blob/main/Samples/AppLifecycle/Instancing/cs-winui-packaged/CsWinUiDesktopInstancing/CsWinUiDesktopInstancing/Program.cs
    public class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            bool isRedirect = DecideRedirection();
            if (!isRedirect)
            {
                Application.Start(p =>
                {
                    var context = new DispatcherQueueSynchronizationContext(
                        DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    _ = new App();
                });
            }
            return 0;
        }
        private static bool DecideRedirection()
        {
            bool isRedirect = false;
            AppActivationArguments args = AppInstance.GetCurrent().GetActivatedEventArgs();
            ExtendedActivationKind kind = args.Kind;
            AppInstance keyInstance = AppInstance.FindOrRegisterForKey("41871fdf-1053-4694-a3fb-38330f65567b");

            if (keyInstance.IsCurrent)
            {
                keyInstance.Activated += OnActivated;
            }
            else
            {
                isRedirect = true;
                RedirectActivationTo(args, keyInstance);
            }
            return isRedirect;
        }
        // Do the redirection on another thread, and use a non-blocking
        // wait method to wait for the redirection to complete.
        public static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
        {
            var redirectSemaphore = new Semaphore(0, 1);
            Task.Run(() =>
            {
                keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
                redirectSemaphore.Release();
            });
            redirectSemaphore.WaitOne();
        }
        private static void OnActivated(object sender, AppActivationArguments args)
        {
            //ExtendedActivationKind kind = args.Kind;
            //if (kind != ExtendedActivationKind.StartupTask)
            //App app = Application.Current as App;
            //app?.Activate();
            //var launchArgs = args.Data as Windows.ApplicationModel.Activation.LaunchActivatedEventArgs;
            if (App.Current is App xApp && xApp.AppWindow != null && xApp.AppWindow is MainWindow mainWindow)
            {
                mainWindow.TryActivate();
            }
        }
    }
}

