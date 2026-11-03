using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Using_quality_analysis_tools
{
    public partial class MainWindow : Window
    {
        // Коллекция для вкладки с визуальным деревом
        public ObservableCollection<SampleItem> Items { get; } = new ObservableCollection<SampleItem>();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this; // для привязки ItemsSource="{Binding Items}"
            for (int i = 0; i < 10; i++)
                Items.Add(new SampleItem { Name = $"Элемент {i}" });
        }

        // ---------- Вкладка 1: CPU / UI Thread ----------
        private void RunSyncHeavyOperation(object sender, RoutedEventArgs e)
        {
            // Синхронный тяжёлый цикл – блокирует UI-поток
            for (int i = 0; i < 1_000_000_000; i++)
            {
                double result = Math.Sqrt(i);
            }
            MessageBox.Show("Синхронная операция завершена. UI был заблокирован.");
        }

        private async void RunAsyncHeavyOperation(object sender, RoutedEventArgs e)
        {
            AsyncProgressBar.Visibility = Visibility.Visible;
            AsyncProgressBar.IsIndeterminate = true;

            await Task.Run(() =>
            {
                for (int i = 0; i < 1_000_000_000; i++)
                {
                    double result = Math.Sqrt(i);
                }
            });

            AsyncProgressBar.Visibility = Visibility.Collapsed;
            MessageBox.Show("Асинхронная операция завершена. UI оставался отзывчивым.");
        }

        // ---------- Вкладка 2: Memory Leaks ----------
        private void CreateStaticEventLeak(object sender, RoutedEventArgs e)
        {
            // Создаём «тяжёлый» объект и подписываем его на статическое событие.
            // Ссылка на объект остаётся в статическом событии навсегда → утечка.
            var leaky = new LeakySubscriber();
            LeakySubscriber.StaticEvent += leaky.OnStaticEvent;
            // Мы не отписываемся и не сохраняем ссылку – объект недоступен, но жив.
        }

        private void StartLeakyDispatcherTimer(object sender, RoutedEventArgs e)
        {
            // DispatcherTimer, который не останавливается, удерживает экземпляр окна/объекта.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, args) =>
            {
                // Бессмысленная работа, но таймер продолжает жить
                var _ = DateTime.Now;
            };
            timer.Start();
            // Ссылка на timer теряется, но он продолжает работать и удерживать подписку.
        }

        // ---------- Вкладка 3: Visual Tree / Data Binding ----------
        private void AddItemToList(object sender, RoutedEventArgs e)
        {
            Items.Add(new SampleItem { Name = $"Элемент {Items.Count}" });
        }

        private void AddManyItemsToList(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 1000; i++)
            {
                Items.Add(new SampleItem { Name = $"Массовый элемент {Items.Count}" });
            }
        }
    }

    // Вспомогательные классы

    public class SampleItem
    {
        public string Name { get; set; }
    }

    // Класс, который «утекает» из-за статического события
    public class LeakySubscriber
    {
        public static event EventHandler StaticEvent;

        private byte[] _payload = new byte[10 * 1024 * 1024]; // 10 МБ

        public void OnStaticEvent(object sender, EventArgs e)
        {
            // Обработчик намеренно ничего не делает
        }

        public static void RaiseEvent()
        {
            StaticEvent?.Invoke(null, EventArgs.Empty);
        }
    }
}