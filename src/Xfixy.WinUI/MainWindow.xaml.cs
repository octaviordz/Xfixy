// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.FSharp.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ReactiveUI;
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
        private readonly App _app = (App)Application.Current;

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
#pragma warning disable CA1822 // Mark members as static
        public string ScriptsLocation => Funcs.GetScriptsLocation();
        public string AppDomainBaseDirectory => AppDomain.CurrentDomain.BaseDirectory;
#pragma warning restore CA1822 // Mark members as static
        private FSharpFunc<Control.WorkerProcessStatus, Unit> WorkerProcessStatusFunc => FuncConvert.FromAction<Control.WorkerProcessStatus>(WorkerProcessStatus);
        public MainWindow()
        {
            InitializeComponent();

            Control.CheckWorkerProcess(WorkerProcessStatusFunc);
            WorkingDirectoryTextBox.Text = Environment.CurrentDirectory;
#if DEBUG
            MessageItems.Add(new("Test m1", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m2", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m3", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m4", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m5", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m6", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m7", DateTime.Now, HorizontalAlignment.Left));
#endif

            _unsubscriber =
                _app.Worker.OnMessage
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(this);

            _appWindow = GetAppWindowForCurrentWindow();
            void OnClosing(object sender, AppWindowClosingEventArgs e)
            {
                Unsubscribe();
                var app = (App)Application.Current;
                //app.WorkerCancellationTokenSource?.Cancel();
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
        private async void EnableStartupTaskButton_Click(object sender, RoutedEventArgs e)
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
        private void WorkerProcessStatus(Control.WorkerProcessStatus status)
        {
            if (status.IsStopped)
            {
                StartStopButton.Content = "Start worker.";
            }
            else if (status.IsRunning)
            {
                StartStopButton.Content = "Stop worker.";
            }
        }
        private async void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            //await _app.Worker.StarStopAsync();
            Control.StartStopWorkerProcess();
            await Task.Delay(200);
            Control.CheckWorkerProcess(WorkerProcessStatusFunc);
        }

        private void TestAddMessageButton_Click(object sender, RoutedEventArgs e)
        {
            MessageItems.Add(new($"Test {Random.Shared.Next()}", DateTime.Now, HorizontalAlignment.Left));
        }
    }
}
