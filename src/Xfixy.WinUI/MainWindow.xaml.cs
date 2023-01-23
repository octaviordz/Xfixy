// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Xfixy.WinUI
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Microsoft.UI.Xaml.Window, IObserver<string>
    {
        private AppWindow _appWindow;

        #region IObserver

        private IDisposable unsubscriber;

        public void Subscribe(IObservable<string> provider)
        {
            if (provider != null)
                unsubscriber = provider.Subscribe(this);
        }

        public void OnCompleted()
        {
            Console.WriteLine("The Message Provider has completed transmitting data.");
            this.Unsubscribe();
        }

        public void OnError(Exception e)
        {
            Console.WriteLine("The message cannot be determined.");
        }

        public void OnNext(string value)
        {
            Console.WriteLine("The current message is {0}", value);
            DispatcherQueue.TryEnqueue(() =>
            {
                messageTextBox.Text += value;
            });
        }

        public void Unsubscribe()
        {
            unsubscriber?.Dispose();
        }
        #endregion


        public MainWindow()
        {
            this.InitializeComponent();

            var app = (App)Application.Current;

            Subscribe(app.Xfixy.OnMessage);

            _appWindow = GetAppWindowForCurrentWindow();
            _appWindow.Closing += OnClosing;
        }

        private async void OnClosing(object sender, AppWindowClosingEventArgs e)
        {
            // https://stackoverflow.com/questions/73697961/how-to-save-data-on-application-close-or-entered-background-in-winui3-app
            e.Cancel = true;
            this.Unsubscribe();

            var app = (App)Application.Current;
            app.WorkerCancellationTokenSource?.Cancel();
            await Task.Delay(100);

            this.Close();
        }


        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId myWndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(myWndId);
        }
    }
}
