// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
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
        private AppWindow _appWindow;
        private string messageInp;
        // TODO: Look for a way to implement a "inverted" ListView.
        // https://github.com/AvaloniaUI/Avalonia/discussions/7596 (Didn't work)
        public ObservableCollection<Message> MessageItems { get; set; } = new();
        public ObservableCollection<Message> OutputItems { get; set; } = new();
        #region IObserver

        private IDisposable unsubscriber;

        public void Subscribe(IObservable<string> provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            Unsubscribe();
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
            // https://devblogs.microsoft.com/oldnewthing/20190328-00/?p=102368
            Console.WriteLine("The current message is {0}", value);
            DispatcherQueue.TryEnqueue(() =>
            {
                messageInp = value;
                AddMessage();
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

#if DEBUG
            MessageItems.Add(new("Test m1", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m2", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m3", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m4", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m5", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m6", DateTime.Now, HorizontalAlignment.Left));
            MessageItems.Add(new("Test m7", DateTime.Now, HorizontalAlignment.Left));
#endif
            Subscribe(app.Xfixy.OnMessage);

            //this.WhenAnyValue(x => x.SearchQuery)
            //    .Throttle(TimeSpan.FromSeconds(0.8), RxApp.TaskpoolScheduler)
            //    .Select(query => query?.Trim())
            //    .DistinctUntilChanged()
            //    .Where(query => !string.IsNullOrWhiteSpace(query))
            //    .ObserveOn(RxApp.MainThreadScheduler)
            //    .InvokeCommand(ExecuteSearch);

            _appWindow = GetAppWindowForCurrentWindow();
            _appWindow.Closing += OnClosing; // Unsubscribe
        }

        private void AddMessage()
        {
            MessageItems.Add(
                new Message(messageInp, DateTime.Now, HorizontalAlignment.Left)
                );
        }

        private void OnClosing(object sender, AppWindowClosingEventArgs e)
        {
            //e.Cancel = true;
            this.Unsubscribe();
            var app = (App)Application.Current;
            app.WorkerCancellationTokenSource?.Cancel();
            //await Task.Delay(100);
            //this.Close();
        }
        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId myWndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(myWndId);
        }
    }
}
