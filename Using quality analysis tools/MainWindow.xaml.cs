using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Using_quality_analysis_tools
{
    /// <summary>
    /// Демонстрация инструментов анализа качества Visual Studio.
    /// Каждая кнопка намеренно воспроизводит определённую проблему,
    /// которую студент должен обнаружить соответствующим инструментом.
    /// </summary>
    public partial class MainWindow : Window
    {
        // ---------- Демонстрационные структуры ----------

        // Статическое событие — держит подписчиков вечно (утечка памяти)
        public static event EventHandler LeakyEvent;

        // Статический кэш — растёт и не очищается (утечка управляемой памяти)
        private static readonly List<byte[]> StaticCache = new List<byte[]>();

        // Блокировки для сценария deadlock
        private readonly object _lock1 = new object();
        private readonly object _lock2 = new object();

        // Счётчик фоновых задач (для журнала)
        private int _backgroundTaskId = 0;

        public MainWindow()
        {
            InitializeComponent();

            // Намеренная утечка: статическое событие живёт всё время жизни AppDomain
            LeakyEvent += OnLeakyEventHandler;
        }

        private void OnLeakyEventHandler(object sender, EventArgs e) { }

        // ---------- Жизненный цикл ----------

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Log($"Загружено. Потоков в процессе: {Process.GetCurrentProcess().Threads.Count}");
            Log($"Управляемая память: {GC.GetTotalMemory(false) / 1024 / 1024} МБ");
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Намеренно НЕ отписываемся от LeakyEvent — это и есть утечка.
            Log("Окно закрывается. LeakyEvent по-прежнему держит подписчиков!");
        }

        // ---------- Журналирование ----------

        private void Log(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Log(message)));
                return;
            }
            var entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            LogList.Items.Insert(0, entry);
            if (LogList.Items.Count > 300)
                LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }

        // =========================================================
        //  1. Performance Profiler (CPU)
        // =========================================================

        // Тяжёлая работа в UI-потоке: UI замирает, CPU spike видно в Performance Profiler
        private void StartLongOperation(object sender, RoutedEventArgs e)
        {
            Log("!!! Тяжёлая работа В UI-ПОТОКЕ. UI будет зависать. Смотрите Performance Profiler.");
            var sw = Stopwatch.StartNew();
            long sum = 0;
            for (int i = 0; i < 100_000_000; i++)
                sum += (long)Math.Sqrt(i);
            sw.Stop();
            Log($"UI-блокировка завершена за {sw.ElapsedMilliseconds} мс (sum={sum})");
        }

        // Правильный вариант с фоновым потоком
        private void StartBackgroundOperation(object sender, RoutedEventArgs e)
        {
            int id = ++_backgroundTaskId;
            Log($"Фоновая задача #{id} запущена...");
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                long sum = 0;
                for (int i = 0; i < 100_000_000; i++)
                    sum += (long)Math.Sqrt(i);
                sw.Stop();
                Log($"Фоновая задача #{id} завершена за {sw.ElapsedMilliseconds} мс");
            });
        }

        // Современный async/await — не блокирует UI-поток и легко читается
        private async void StartAsyncOperation(object sender, RoutedEventArgs e)
        {
            Log("Async-операция запущена...");
            var sw = Stopwatch.StartNew();
            long sum = await Task.Run(() =>
            {
                long s = 0;
                for (int i = 0; i < 50_000_000; i++) s += (long)Math.Sqrt(i);
                return s;
            });
            sw.Stop();
            Log($"Async завершена за {sw.ElapsedMilliseconds} мс, sum={sum}");
        }

        // =========================================================
        //  2. Memory Usage
        // =========================================================

        private void CreateEventLeak(object sender, RoutedEventArgs e)
        {
            // Каждое нажатие добавляет 1000 «мёртвых» подписчиков на статическое событие
            for (int i = 0; i < 1000; i++)
                LeakyEvent += OnLeakyEventHandler;
            var count = LeakyEvent?.GetInvocationList().Length ?? 0;
            Log($"Добавлено 1000 подписчиков на LeakyEvent. Всего подписчиков: {count}");
        }

        private void CreateStaticListLeak(object sender, RoutedEventArgs e)
        {
            const int add = 20; // 20 x 1 МБ = 20 МБ за нажатие
            for (int i = 0; i < add; i++)
                StaticCache.Add(new byte[1024 * 1024]);

            Log($"StaticCache: {StaticCache.Count} элементов (~{StaticCache.Count} МБ). " +
                "Смотрите Memory Usage → сделайте Snapshot и сравните.");
        }

        private void ForceGarbageCollection(object sender, RoutedEventArgs e)
        {
            long before = GC.GetTotalMemory(forceFullCollection: true);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long after = GC.GetTotalMemory(forceFullCollection: true);
            Log($"GC выполнен. Было: {before / 1024 / 1024} МБ, стало: {after / 1024 / 1024} МБ " +
                "(разница — это и есть «утечка», которую не убирает GC).");
        }

        // =========================================================
        //  3. Live Visual Tree / Live Property Explorer
        // =========================================================

        // Глубокая вложенность — хорошо видна в Live Visual Tree
        private void AddDeepVisualTree(object sender, RoutedEventArgs e)
        {
            Log("Добавление дерева из 30 вложенных Border...");
            var root = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                Margin = new Thickness(2)
            };
            var current = root;
            for (int i = 0; i < 30; i++)
            {
                var child = new Border
                {
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.LightBlue,
                    Margin = new Thickness(2),
                    Child = new TextBlock { Text = $"Уровень {i}", FontSize = 10 }
                };
                current.Child = child;
                current = child;
            }
            DynamicContentPanel.Children.Add(root);
            Log("Готово. Откройте Live Visual Tree для исследования иерархии.");
        }

        // Масса битых привязок — сообщения в окне Output (Binding Errors)
        private void AddMassiveBindings(object sender, RoutedEventArgs e)
        {
            Log("Добавление 500 TextBox с заведомо неверными привязками...");
            var panel = new StackPanel();
            for (int i = 0; i < 500; i++)
            {
                var tb = new TextBox();
                tb.SetBinding(TextBox.TextProperty,
                    new Binding($"NonExistentProperty_{i}")); // свойства нет → ошибка привязки
                panel.Children.Add(tb);
            }
            DynamicContentPanel.Children.Add(panel);
            Log("Откройте окно Output и включите фильтр 'Binding' — увидите поток ошибок.");
        }

        // Корректная привязка через INotifyPropertyChanged + ObservableCollection
        private void AddProperBinding(object sender, RoutedEventArgs e)
        {
            Log("Пример корректной MVVM-привязки...");
            var items = new ObservableCollection<DemoItem>();
            for (int i = 0; i < 25; i++)
                items.Add(new DemoItem { Name = $"Элемент {i}" });

            var listBox = new ListBox
            {
                Height = 200,
                Width = 320,
                ItemsSource = items,
                DisplayMemberPath = nameof(DemoItem.Name)
            };
            DynamicContentPanel.Children.Add(listBox);

            // Демонстрация Live Property Explorer: переключите Name у одного элемента
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            int tick = 0;
            timer.Tick += (s, a) =>
            {
                if (tick >= 10) { timer.Stop(); return; }
                items[tick % items.Count].Name = $"Обновлён {tick++}";
            };
            timer.Start();
            Log("Откройте Live Property Explorer: наблюдайте изменения Name.");
        }

        // =========================================================
        //  4. Рендеринг / анимации
        // =========================================================

        private void StartAnimationStorm(object sender, RoutedEventArgs e)
        {
            Log("Запуск 100 непрерывных анимаций...");
            var canvas = new Canvas { Height = 300, Background = Brushes.Black };
            DynamicContentPanel.Children.Add(canvas);

            var rnd = new Random();
            for (int i = 0; i < 100; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(Color.FromRgb(
                        (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256)))
                };
                Canvas.SetLeft(rect, rnd.Next(300));
                Canvas.SetTop(rect, rnd.Next(300));
                canvas.Children.Add(rect);

                rect.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation
                {
                    From = 0,
                    To = 500,
                    Duration = TimeSpan.FromSeconds(2),
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true
                });
                rect.BeginAnimation(Canvas.TopProperty, new DoubleAnimation
                {
                    From = 0,
                    To = 250,
                    Duration = TimeSpan.FromSeconds(1.5),
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true
                });
            }
            Log("Анимации запущены. Смотрите CPU/GPU в Performance Profiler (Timeline).");
        }

        // Layout Thrashing: 1000 синхронных UpdateLayout подряд
        private void LayoutThrashing(object sender, RoutedEventArgs e)
        {
            Log("Layout Thrashing: 1000 принудительных перекомпоновок...");
            var border = new Border
            {
                Width = 100,
                Height = 100,
                Background = Brushes.Orange,
                Margin = new Thickness(4)
            };
            DynamicContentPanel.Children.Add(border);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                border.Width = 100 + (i % 50);
                border.Height = 100 + (i % 50);
                border.UpdateLayout(); // заставляет WPF пересчитывать layout немедленно
            }
            sw.Stop();
            Log($"Layout thrashing занял {sw.ElapsedMilliseconds} мс. " +
                "В Performance Profiler видно Layout/Measure/Arrange.");
        }

        // =========================================================
        //  5. Многопоточность (Debug → Threads)
        // =========================================================

        private void DeadlockDemo(object sender, RoutedEventArgs e)
        {
            Log("Сценарий потенциального deadlock (Debug → Threads)...");

            var t1 = new Thread(() =>
            {
                lock (_lock1)
                {
                    Thread.Sleep(100);
                    lock (_lock2) { Log("Поток 1 захватил оба lock."); }
                }
            });

            var t2 = new Thread(() =>
            {
                lock (_lock2)
                {
                    Thread.Sleep(100);
                    lock (_lock1) { Log("Поток 2 захватил оба lock."); }
                }
            });

            t1.IsBackground = true;
            t2.IsBackground = true;
            t1.Start();
            t2.Start();
            Log("Запущены два потока. Через 100 мс возможен взаимный захват — " +
                "смотрите Debug → Windows → Threads и стек каждого потока.");
        }

        // =========================================================
        //  6. Сброс состояния
        // =========================================================

        private void ClearAll(object sender, RoutedEventArgs e)
        {
            DynamicContentPanel.Children.Clear();
            Log("UI очищено. Но статические утечки (LeakyEvent, StaticCache) сохраняются!");
        }

        private void ResetLeaks(object sender, RoutedEventArgs e)
        {
            // Корректная отписка — способ борьбы с утечкой через события
            if (LeakyEvent != null)
            {
                foreach (var d in LeakyEvent.GetInvocationList())
                    LeakyEvent -= (EventHandler)d;
            }
            StaticCache.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Log("Утечки сброшены: подписки сняты, StaticCache очищен, GC вызван.");
        }
    }

    // =========================================================
    //  Вспомогательный класс для демонстрации правильных привязок.
    //  (В одном файле — по требованию задания.)
    // =========================================================
    public class DemoItem : INotifyPropertyChanged
    {
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}