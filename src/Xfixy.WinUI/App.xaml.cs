// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using WinRT.Interop;
using Xfixy.Server;
using IOPath = System.IO.Path;
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

// https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions#start-an-executable-file-when-users-log-into-windows
namespace Xfixy.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Worker xfixy;
        internal Worker Xfixy
        {
            get
            {
                return xfixy;
            }
        }
        internal CancellationTokenSource WorkerCancellationTokenSource { get; set; }
        public Window AppWindow
        {
            get { return m_window; }
            private set { }
        }
        private IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                    .ConfigureServices((hostContext, services) =>
                    {
                        services.AddSingleton<Worker>();
                        services.AddSingleton<IHostedService>(p => p.GetService<Worker>());
                    })
                    .ConfigureLogging((hostingContext, logging) =>
                    {
                        logging.AddEventSourceLogger();
                    });

            // https://github.com/microsoft/WindowsAppSDK/discussions/2195
            // AppDomain.CurrentDomain.BaseDirectory
            // Environment.GetFolderPath(Environment.SpecialFolder.Startup)
        }
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            IHost host = CreateHostBuilder(args: null).Build();
            var config = host.Services.GetRequiredService<IConfiguration>();
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var fullPath = IOPath.Join(localAppData, "Xfixy", "Ps1-scripts");
            config["Worker:Scripts-Location"] = fullPath;
            var cts = new CancellationTokenSource();
            this.WorkerCancellationTokenSource = cts;
            host.StartAsync(cts.Token);
            this.xfixy = host.Services.GetService<Worker>();
        }
        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // NOTE: WinUI's App.OnLaunched is given a Microsoft.UI.Xaml.LaunchActivatedEventArgs,
            // where the UWPLaunchActivatedEventArgs property will be one of the 
            // Windows.ApplicationModel.Activation.*ActivatedEventArgs types.
            // Conversely, AppInstance.GetActivatedEventArgs will return a
            // Microsoft.Windows.AppLifecycle.AppActivationArguments, where the Data property
            // will be one of the Windows.ApplicationModel.Activation.*ActivatedEventArgs types.
            // NOTE: OnLaunched will always report that the ActivationKind == Launch,
            // even when it isn't.
            Windows.ApplicationModel.Activation.ActivationKind kind = args.UWPLaunchActivatedEventArgs.Kind;

            // NOTE: AppInstance is ambiguous between
            // Microsoft.Windows.AppLifecycle.AppInstance and
            // Windows.ApplicationModel.AppInstance
            var currentInstance = AppInstance.GetCurrent();
            if (currentInstance != null)
            {
                // AppInstance.GetActivatedEventArgs will report the correct ActivationKind,
                // even in WinUI's OnLaunched.
                AppActivationArguments activationArgs = currentInstance.GetActivatedEventArgs();
                if (activationArgs != null)
                {
                    ExtendedActivationKind extendedKind = activationArgs.Kind;
                }
            }
            m_window = new MainWindow();
            m_window.Activate();
        }
        //[DllImport("user32.dll")]
        //private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        //[DllImport("user32.dll")]
        //private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        //private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        //private static IntPtr FindWindowByProcessId(uint processId)
        //{
        //    IntPtr hWnd = IntPtr.Zero;
        //    EnumWindows((IntPtr hwnd, IntPtr lParam) =>
        //    {
        //        GetWindowThreadProcessId(hwnd, out uint windowProcessId);
        //        if (windowProcessId == processId)
        //        {
        //            hWnd = hwnd;
        //            return false;
        //        }
        //        return true;
        //    }, IntPtr.Zero);
        //    return hWnd;
        //}
        //private AppWindow GetAppWindowForCurrentWindow()
        //{
        //    IntPtr hWnd = FindWindowByProcessId(AppInstance.GetCurrent().ProcessId);
        //    WindowId myWndId = Win32Interop.GetWindowIdFromWindow(hWnd);
        //    return AppWindow.GetFromWindowId(myWndId);
        //}
        //internal void Activate()
        //{
        //    var appWindow = GetAppWindowForCurrentWindow();
        //    appWindow?.Show(true);
        //}

        private Window m_window;
    }
}
