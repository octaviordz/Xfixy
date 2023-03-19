// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Xfixy.Server;
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
        internal WorkerProxy Worker { get; }
        internal CancellationTokenSource WorkerCancellationTokenSource { get; set; }
        public Window AppWindow
        {
            get { return _window; }
            private set { }
        }
        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                    .ConfigureServices((hostContext, services) =>
                    {
                        services.AddSingleton<WorkerProxy>();
                        services.AddSingleton<IHostedService>(p => p.GetService<WorkerProxy>());
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
            InitializeComponent();
            IHost host = CreateHostBuilder(args: null).Build();
            IConfiguration config = host.Services.GetRequiredService<IConfiguration>();
            config["Worker:Scripts-Location"] = Funcs.GetScriptsLocation();
            WorkerCancellationTokenSource = new CancellationTokenSource();
            host.StartAsync(WorkerCancellationTokenSource.Token);
            Worker = host.Services.GetService<WorkerProxy>();
        }
        public void StopWorkerProxy() => WorkerCancellationTokenSource.Cancel();
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
            // Windows.ApplicationModel.Activation.ActivationKind kind = args.UWPLaunchActivatedEventArgs.Kind;

            // NOTE: AppInstance is ambiguous between
            // Microsoft.Windows.AppLifecycle.AppInstance and
            // Windows.ApplicationModel.AppInstance
            //var currentInstance = AppInstance.GetCurrent();
            //if (currentInstance != null)
            //{
            //    // AppInstance.GetActivatedEventArgs will report the correct ActivationKind,
            //    // even in WinUI's OnLaunched.
            //    AppActivationArguments activationArgs = currentInstance.GetActivatedEventArgs();
            //    if (activationArgs != null)
            //    {
            //        ExtendedActivationKind extendedKind = activationArgs.Kind;
            //    }
            //}
            _window = new MainWindow();
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 900, Height = 800 });

            _window.Activate();
        }

        private Window _window;
    }
}
