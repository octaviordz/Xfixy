// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using WinRT.Interop;

using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Windows.UI.WebUI;

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
    // https://github.com/AvaloniaUI/Avalonia/discussions/7596
    /// <summary>
    ///     Represents a first-in, first-out collection of objects, and supports observation
    ///     notifications and a max buffer size.
    /// </summary>
    /// <typeparam name="T"> The Type to wrap. </typeparam>
    public class ObservableQueue<T> : Queue<T>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        private int bufferSize = -1;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObservableQueue{T}" /> class that is
        ///     empty and has the default initial capacity.
        /// </summary>
        public ObservableQueue()
            : base()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObservableQueue{T}" /> class that
        ///     contains elements copied from the specified collection and has sufficient capacity
        ///     to accommodate the number of elements copied.
        /// </summary>
        public ObservableQueue(IEnumerable<T> collection)
            : base(collection)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObservableQueue{T}" /> class that is
        ///     empty and has the specified initial capacity. Also sets the buffer to the specified capacity.
        /// </summary>
        public ObservableQueue(int capacity)
            : base(capacity)
        {
            this.BufferSize = capacity;
        }

        /// <summary>
        ///     Gets or sets the maximum number of elements allowed in the queue.
        /// </summary>
        public int BufferSize
        {
            get => this.bufferSize;
            set
            {
                if (value < 0)
                {
                    this.bufferSize = -1;
                    return;
                }

                this.bufferSize = value;
                this.FixSize();
            }
        }

        public virtual event NotifyCollectionChangedEventHandler? CollectionChanged;

        public virtual event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        ///     Ensures that the queue does not exceed the buffer size, if there is one.
        /// </summary>
        private void FixSize()
        {
            if (this.BufferSize > -1)
            {
                while (this.Count > this.BufferSize)
                {
                    this.Dequeue();
                }
            }
        }

        /// <summary>
        ///     Triggers a CollectionChanged event.
        /// </summary>
        /// <param name="action"> The action that has taken place. </param>
        /// <param name="item"> The item that changed. </param>
        /// <param name="index"> The index where the item was when it changed. </param>
        protected virtual void OnCollectionChanged(NotifyCollectionChangedAction action, T item, int index)
        {
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                action
                , item
                , index)
            );

            OnPropertyChanged(nameof(Count));
        }

        /// <summary>
        ///     Triggers a PropertyChanged event.
        /// </summary>
        /// <param name="propertyName"> The name of the property that changed. </param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <inheritdoc />
        public virtual new void Clear()
        {
            base.Clear();
            this.OnCollectionChanged(NotifyCollectionChangedAction.Reset, default!, -1);
        }

        /// <inheritdoc />
        public virtual new T Dequeue()
        {
            var item = base.Dequeue();
            this.OnCollectionChanged(NotifyCollectionChangedAction.Remove, item, this.Count);
            return item;
        }

        /// <inheritdoc />
        public virtual new void Enqueue(T item)
        {
            base.Enqueue(item);
            this.OnCollectionChanged(NotifyCollectionChangedAction.Add, item, 0);
            this.FixSize();
        }

        /// <summary>
        ///     An alias for Dequeue.
        /// </summary>
        /// <returns> The item that was removed. </returns>
        public T Pop()
        {
            return this.Dequeue();
        }

        /// <summary>
        ///     An alias for Enqueue.
        /// </summary>
        /// <param name="item"> </param>
        public void Push(T item)
        {
            this.Enqueue(item);
        }
    }

    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Microsoft.UI.Xaml.Window, IObserver<string>
    {
        private AppWindow _appWindow;
        private string messageInp;
        public ObservableQueue<Message> OutputItems { get; set; } = new ObservableQueue<Message>();
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
                messageInp = value;
                AddItemToEnd();
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

            //OutputItems.Push(new("Test 1", DateTime.Now, HorizontalAlignment.Left));
            //OutputItems.Push(new("Test 2", DateTime.Now, HorizontalAlignment.Left));
            Subscribe(app.Xfixy.OnMessage);

            _appWindow = GetAppWindowForCurrentWindow();
            _appWindow.Closing += OnClosing;

        }

        private void AddItemToEnd()
        {
            //OutputItems.Push(
            //    new Message(messageInp, DateTime.Now, HorizontalAlignment.Left)
            //    );
            InvertedListView.Items.Add(
                new Message(messageInp, DateTime.Now, HorizontalAlignment.Left)
                );
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
