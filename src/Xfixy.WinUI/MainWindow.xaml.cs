// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.UI.Popups;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Xfixy.WinUI
{
    public class Message
    {
        public string MsgText { get; private set; }
        public DateTime MsgDateTime { get; private set; }
        public HorizontalAlignment MsgAlignment { get; set; }
        public Message(string text, DateTime dateTime, HorizontalAlignment align)
        {
            MsgText = text;
            MsgDateTime = dateTime;
            MsgAlignment = align;
        }
        public override string ToString()
        {
            return MsgDateTime.ToString() + " " + MsgText;
        }
    }
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Microsoft.UI.Xaml.Window, IObserver<string>
    {
        private readonly AppWindow _appWindow;
        private string _messageInp;
        // TODO: Look for a way to implement a "inverted" ListView.
        // https://github.com/AvaloniaUI/Avalonia/discussions/7596 (Didn't work)
        public ObservableCollection<Message> MessageItems { get; set; } = new();
        #region IObserver

        private readonly IDisposable _unsubscriber = null;

        public void OnCompleted()
        {
            Debug.WriteLine("The Message Provider has completed transmitting data.");
            Unsubscribe();
        }

        public void OnError(Exception e)
        {
            Debug.WriteLine("The message cannot be determined.");
        }

        public void OnNext(string value)
        {
            // https://devblogs.microsoft.com/oldnewthing/20190328-00/?p=102368
            Debug.WriteLine("The current message is {0}", value);
            _messageInp = value;
            AddMessage();
        }

        public void Unsubscribe()
        {
            _unsubscriber?.Dispose();
        }
        #endregion

        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            MessageItems.Add(new("Test m1", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m2", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m3", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m4", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m5", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m6", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m7", DateTime.Now, HorizontalAlignment.Left));
#endif

            var app = (App)Application.Current;

            _unsubscriber =
                app.Worker.OnMessage
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(this);

            _appWindow = GetAppWindowForCurrentWindow();
            void OnClosing(object sender, AppWindowClosingEventArgs e)
            {
                Unsubscribe();
                var app = (App)Application.Current;
                app.WorkerCancellationTokenSource?.Cancel();
            }
            _appWindow.Closing += OnClosing; // Unsubscribe
        }
        private void AddMessage()
        {
            MessageItems.Add(
                new Message(_messageInp, DateTime.Now, HorizontalAlignment.Left)
                );
        }
        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId myWndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(myWndId);
        }
        //[LibraryImport("user32.dll")]
        //[return: MarshalAs(UnmanagedType.Bool)]
        //internal static partial bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        public void TryActivate()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // Bug: https://github.com/microsoft/microsoft-ui-xaml/issues/7595
                Activate();
                SetForegroundWindow(WindowNative.GetWindowHandle(this));
            });
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            // https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask?view=winrt-22621
            StartupTask startupTask = null;
            try
            {
                startupTask = await StartupTask.GetAsync("XfixyStartupTask");
            }
            catch (ArgumentException)
            {
            }
            catch (COMException)
            {
            }
            switch (startupTask?.State)
            {
                case StartupTaskState.Disabled:
                    // Task is disabled but can be enabled.
                    StartupTaskState newState = await startupTask.RequestEnableAsync(); // ensure that you are on a UI thread when you call RequestEnableAsync()
                    Debug.WriteLine("Request to enable startup, result = {0}", newState);
                    break;
                case StartupTaskState.DisabledByUser:
                    // Task is disabled and user must enable it manually.
                    MessageDialog dialog = new(
                        "You have disabled this app's ability to run " +
                        "as soon as you sign in, but if you change your mind, " +
                        "you can enable this in the Startup tab in Task Manager.",
                        "XfixyStartup");
                    await dialog.ShowAsync();
                    break;
                case StartupTaskState.DisabledByPolicy:
                    Debug.WriteLine("Startup disabled by group policy, or not supported on this device.");
                    break;
                case StartupTaskState.Enabled:
                    Debug.WriteLine("Startup is enabled.");
                    break;
            }
        }
    }
}
