// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using System;
using System.Threading;
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
        private IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                    //.ConfigureServices((hostContext, services) => services.AddHostedService<Worker>());
                    .ConfigureServices((hostContext, services) =>
                    {
                        services.AddSingleton<Worker>();
                        services.AddSingleton<IHostedService>(p => p.GetService<Worker>());
                    });
                    //https://github.com/microsoft/WindowsAppSDK/discussions/2195
                    //.UseContentRoot(Environment.CurrentDirectory);
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
            var value = config?.GetValue<int>("Worker:Delay", 4000);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var fullPath = IOPath.Join(localAppData, "Xfixy", "ps1-scripts");
            config["Worker:Scripts-Path"] = fullPath;
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
            m_window = new MainWindow();
            m_window.Activate();
        }

        private Window m_window;
    }
}
