using Microsoft.Win32;
using slider.Models;
using slider.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;


namespace slider
{
    public partial class MainWindow : Window
    {
        private const string DefaultTransitionEffect = "Затухание";
        private List<PlaylistPeriod> periods = new List<PlaylistPeriod>();
        private PlaylistPeriod? selectedPeriod = null;
        private Point _dragStartPoint;
        private SlideItem? _draggedSlideItem;
        private DispatcherTimer? autoSaveDelayTimer;
        private bool isLoadingPlaylist = false;
        private SliderWindow? sliderWindow = null;
        private readonly FfmpegOutputService ffmpegOutputService = new();
        private readonly DispatcherTimer ffmpegPlaybackTimer = new();
        private List<SlideItem> ffmpegSlides = new();
        private StreamSettings currentStreamSettings = new StreamSettings();
        private const int DefaultSlideDurationSeconds = 5;
        private const int DefaultAutoSaveMinutes = 5;
        private const string DefaultPeriodName = "Новый период";
        private const string SelectPeriodFirstMessage = "Сначала выбери период";
        private const string SelectImageFirstMessage = "Сначала выбери изображение";
        private const string ConfirmDeleteTitle = "Подтверждение";
        private readonly Dictionary<string, BitmapImage> imageCache = new();
        public MainWindow()
        {
            InitializeComponent();
            InitializeAutoSaveDelayTimer();
            InitializeDefaultPeriods();
            RefreshPeriodsList();
            RefreshMediaGrid();
            UpdateStatus();

           

        }



        // =========================
        // ИНИЦИАЛИЗАЦИЯ
        // =========================


        private readonly string lastPlaylistInfoFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_playlist.txt");
        private PlaylistData BuildPlaylistData()
        {
            int autoSaveMinutes = int.TryParse(AutoSaveMinutesTextBox.Text, out int parsedMinutes)
            ? parsedMinutes
            : DefaultAutoSaveMinutes;

            if (autoSaveMinutes <= 0)
                autoSaveMinutes = DefaultAutoSaveMinutes;

            return new PlaylistData
            {
                PlaylistPath = PlaylistPathTextBox.Text.Trim(),
                AutoSaveEnabled = AutoSaveCheckBox.IsChecked == true,
                AutoSaveMinutes = autoSaveMinutes,
                Periods = periods
            };
        }



        private void InitializeAutoSaveDelayTimer()
        {
            autoSaveDelayTimer = new DispatcherTimer();
            autoSaveDelayTimer.Interval = TimeSpan.FromSeconds(2);
            autoSaveDelayTimer.Tick += AutoSaveDelayTimer_Tick;
        }

        private void AutoSaveDelayTimer_Tick(object? sender, EventArgs e)
        {
            if (autoSaveDelayTimer != null)
                autoSaveDelayTimer.Stop();

            PerformAutoSave();
        }

        private void PeriodNameTextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
                return;

            if ((sender as FrameworkElement)?.DataContext is not PlaylistPeriod period)
                return;

            var container = FindParent<Grid>(sender as DependencyObject);
            if (container == null)
                return;

            var textBlock = container.FindName("PeriodNameTextBlock") as TextBlock;
            var textBox = container.FindName("PeriodNameEditTextBox") as TextBox;

            if (textBlock != null)
                textBlock.Visibility = Visibility.Collapsed;

            if (textBox != null)
            {
                textBox.Visibility = Visibility.Visible;
                textBox.Focus();
                textBox.SelectAll();
            }

            e.Handled = true;
        }

        private void LoadPlaylistFromFile(string filePath, bool saveAsLast)
        {
            var data = PlaylistFileService.Load(filePath);

            PlaylistPathTextBox.Text = filePath;

            ApplyPlaylistData(data);

            if (saveAsLast)
                SaveLastPlaylistPath(filePath);
        }

        private void PeriodNameEditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            if (e.Key == Key.Enter)
            {
                CommitPeriodNameEdit(textBox);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelPeriodNameEdit(textBox);
                e.Handled = true;
            }
        }

        private void PeriodNameEditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                CommitPeriodNameEdit(textBox);
            }
        }

        private void CommitPeriodNameEdit(TextBox textBox)
        {
            if ((textBox.DataContext as PlaylistPeriod) is not PlaylistPeriod period)
                return;

            string newName = textBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(newName))
                newName = DefaultPeriodName;

            period.Name = newName;

            var container = FindParent<Grid>(textBox);
            if (container != null)
            {
                var textBlock = container.FindName("PeriodNameTextBlock") as TextBlock;
                var editBox = container.FindName("PeriodNameEditTextBox") as TextBox;

                if (textBlock != null)
                    textBlock.Visibility = Visibility.Visible;

                if (editBox != null)
                    editBox.Visibility = Visibility.Collapsed;
            }

            RefreshPeriodUi();
            UpdateStatus("Имя периода изменено");
            ScheduleAutoSave();
        }

        private void CancelPeriodNameEdit(TextBox textBox)
        {
            if ((textBox.DataContext as PlaylistPeriod) is PlaylistPeriod period)
            {
                textBox.Text = period.Name;
            }

            var container = FindParent<Grid>(textBox);
            if (container != null)
            {
                var textBlock = container.FindName("PeriodNameTextBlock") as TextBlock;
                var editBox = container.FindName("PeriodNameEditTextBox") as TextBox;

                if (textBlock != null)
                    textBlock.Visibility = Visibility.Visible;

                if (editBox != null)
                    editBox.Visibility = Visibility.Collapsed;
            }
        }
        private void ScheduleAutoSave()
        {
            if (isLoadingPlaylist)
                return;

            if (AutoSaveCheckBox == null || AutoSaveCheckBox.IsChecked != true)
                return;

            if (string.IsNullOrWhiteSpace(PlaylistPathTextBox.Text))
                return;

            if (autoSaveDelayTimer == null)
                return;

            autoSaveDelayTimer.Stop();
            autoSaveDelayTimer.Start();
        }

        private void PerformAutoSave()
        {
            try
            {
                string filePath = PlaylistPathTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(filePath))
                    return;

                var data = BuildPlaylistData();
                PlaylistFileService.Save(filePath, data);

                SaveLastPlaylistPath(filePath);

                StatusTextBlock.Text = "Автосохранение выполнено";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка автосохранения:\n{ex.Message}");
            }
        }

        private void ChangeTimeValue(TextBox textBox, int min, int max, int delta)
        {
            if (!int.TryParse(textBox.Text, out int value))
                value = 0;

            value += delta;

            if (value > max) value = min;
            if (value < min) value = max;

            textBox.Text = value.ToString("00");
        }

        private void StartHourUp_Click(object sender, RoutedEventArgs e)
        {
            ChangeTimeValue(StartHourTextBox, 0, 23, +1);
        }

        private void StartHourDown_Click(object sender, RoutedEventArgs e)
        {
            ChangeTimeValue(StartHourTextBox, 0, 23, -1);
        }

        private void StartMinuteUp_Click(object sender, RoutedEventArgs e)
        {
            ChangeTimeValue(StartMinuteTextBox, 0, 59, +1);
        }

        private void StartMinuteDown_Click(object sender, RoutedEventArgs e)
        {
            ChangeTimeValue(StartMinuteTextBox, 0, 59, -1);
        }

        private void EndHourUp_Click(object sender, RoutedEventArgs e)
        {
            ChangeTimeValue(EndHourTextBox, 0, 23, +1);
        }

        private void EndHourDown_Click(object sender, RoutedEventArgs e)
        {
            ChangeTimeValue(EndHourTextBox, 0, 23, -1);
        }

        private void EndMinuteUp_Click(object sender, RoutedEventArgs e)
        {
            ChangeTimeValue(EndMinuteTextBox, 0, 59, +1);
        }

        private void EndMinuteDown_Click(object sender, RoutedEventArgs e)
        {
            ChangeTimeValue(EndMinuteTextBox, 0, 59, -1);
        }

        private void Hour_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is TextBox tb)
            {
                ChangeTimeValue(tb, 0, 23, e.Delta > 0 ? 1 : -1);
            }
        }

        private void Minute_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is TextBox tb)
            {
                ChangeTimeValue(tb, 0, 59, e.Delta > 0 ? 1 : -1);
            }
        }

        private void ApplyPlaylistData(PlaylistData data)
        {
            isLoadingPlaylist = true;

            try
            {
                periods = data.Periods ?? new List<PlaylistPeriod>();

                if (periods.Count == 0)
                {
                    InitializeDefaultPeriods();
                }
                else
                {
                    selectedPeriod = periods.FirstOrDefault();
                }

                PlaylistPathTextBox.Text = data.PlaylistPath ?? "";
                AutoSaveCheckBox.IsChecked = data.AutoSaveEnabled;
                AutoSaveMinutesTextBox.Text = data.AutoSaveMinutes.ToString();
                RefreshPeriodUi();
                ClearSelectedImageEditor();
                UpdateStatus("Плейлист загружен");
            }
            finally
            {
                isLoadingPlaylist = false;
            }
            RefreshPlaylistHeader();
        }


        private void ImagesDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid dataGrid)
                return;

            _dragStartPoint = e.GetPosition(null);

            DependencyObject? source = e.OriginalSource as DependencyObject;

            var row = FindParent<DataGridRow>(source);
            var cell = FindParent<DataGridCell>(source);

            if (row?.Item is SlideItem slideItem)
            {
                _draggedSlideItem = slideItem;
                return;
            }

            _draggedSlideItem = null;

            if (row == null && cell == null)
            {
                dataGrid.UnselectAll();
                dataGrid.SelectedItem = null;
                ClearSelectedImageEditor();
            }
        }


        private void ImagesDataGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedSlideItem == null)
                return;

            Point currentPos = e.GetPosition(null);

            if (Math.Abs(currentPos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(ImagesDataGrid, _draggedSlideItem, DragDropEffects.Move);
            }
        }

        private void ImagesDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(SlideItem)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;

            ClearDragHighlight();

            var row = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
            if (row != null)
            {
                row.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155"));
                _lastHighlightedRow = row;
            }

            e.Handled = true;
        }

        private DataGridRow? _lastHighlightedRow;

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;

                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        private void ImagesDataGrid_Drop(object sender, DragEventArgs e)
        {

                ClearDragHighlight();

            if (selectedPeriod is not { } period)
                return;

            if (!e.Data.GetDataPresent(typeof(SlideItem)))
                return;

            var droppedData = e.Data.GetData(typeof(SlideItem)) as SlideItem;
            if (droppedData == null)
                return;

            var row = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
            var targetItem = row?.Item as SlideItem;

            if (targetItem == null || ReferenceEquals(droppedData, targetItem))
                return;

            int oldIndex = period.Slides.IndexOf(droppedData);
            int newIndex = period.Slides.IndexOf(targetItem);

            if (oldIndex < 0 || newIndex < 0)
                return;

            period.Slides.RemoveAt(oldIndex);
            period.Slides.Insert(newIndex, droppedData);

            RefreshMediaGrid();
            ImagesDataGrid.SelectedItem = droppedData;
            UpdateStatus("Порядок изображений изменён");
            ScheduleAutoSave();
        }

        private PlaylistData currentPlaylistSettings = new PlaylistData();

        private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            currentPlaylistSettings.PlaylistName = PlaylistNameTextBox.Text;
            currentPlaylistSettings.PlaylistPath = PlaylistPathTextBox.Text;
            currentPlaylistSettings.AutoSaveEnabled = AutoSaveCheckBox.IsChecked == true;

            if (int.TryParse(AutoSaveMinutesTextBox.Text, out int autoSaveMinutes))
                currentPlaylistSettings.AutoSaveMinutes = autoSaveMinutes;

            currentPlaylistSettings.Periods = periods;

            SettingsWindow settingsWindow = new SettingsWindow(currentPlaylistSettings);
            settingsWindow.Owner = this;

            if (settingsWindow.ShowDialog() == true)
            {
                currentPlaylistSettings = settingsWindow.SettingsData;

                PlaylistNameTextBox.Text = currentPlaylistSettings.PlaylistName;
                PlaylistPathTextBox.Text = currentPlaylistSettings.PlaylistPath;
                AutoSaveCheckBox.IsChecked = currentPlaylistSettings.AutoSaveEnabled;
                AutoSaveMinutesTextBox.Text = currentPlaylistSettings.AutoSaveMinutes.ToString();

                RefreshPlaylistHeader();
                UpdateStatus("Настройки сохранены");
                ScheduleAutoSave();
            }
        }

        private void ImagesDataGrid_DragLeave(object sender, DragEventArgs e)
        {
                ClearDragHighlight();
        }

        private void SaveLastPlaylistPath(string filePath)
        {
            File.WriteAllText(lastPlaylistInfoFile, filePath);
        }

        private string GetLastPlaylistPath()
        {
            if (!File.Exists(lastPlaylistInfoFile))
                return "";

            return File.ReadAllText(lastPlaylistInfoFile).Trim();
        }
        private void EditPeriod_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PlaylistPeriod period)
            {
                selectedPeriod = period;
                RefreshPeriodEditor();
                UpdateStatus("Редактирование периода");
            }
        }
        private void PeriodMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void DeletePeriod_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PlaylistPeriod period)
            {
                if (MessageBox.Show($"Удалить период \"{period.Name}\"?",
                    ConfirmDeleteTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    periods.Remove(period);
                    selectedPeriod = periods.FirstOrDefault();

                    RefreshPeriodUi();
                    ClearSelectedImageEditor();

                    UpdateStatus("Период удалён");
                    ScheduleAutoSave();
                }
            }
        }

        private void ApplyPeriodSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPeriod is not { } period)
            {
                MessageBox.Show(SelectPeriodFirstMessage);
                return;
            }

            string name = PeriodNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Введите имя периода");
                return;
            }

            if (StartDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Выбери дату начала");
                return;
            }

            if (EndDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Выбери дату конца");
                return;
            }

            if (!int.TryParse(StartHourTextBox.Text, out int startHour) || startHour < 0 || startHour > 23)
            {
                MessageBox.Show("Часы начала должны быть от 0 до 23");
                return;
            }

            if (!int.TryParse(StartMinuteTextBox.Text, out int startMinute) || startMinute < 0 || startMinute > 59)
            {
                MessageBox.Show("Минуты начала должны быть от 0 до 59");
                return;
            }

            if (!int.TryParse(EndHourTextBox.Text, out int endHour) || endHour < 0 || endHour > 23)
            {
                MessageBox.Show("Часы конца должны быть от 0 до 23");
                return;
            }

            if (!int.TryParse(EndMinuteTextBox.Text, out int endMinute) || endMinute < 0 || endMinute > 59)
            {
                MessageBox.Show("Минуты конца должны быть от 0 до 59");
                return;
            }

            DateTime start = StartDatePicker.SelectedDate.Value.Date
                .AddHours(startHour)
                .AddMinutes(startMinute);

            DateTime end = EndDatePicker.SelectedDate.Value.Date
                .AddHours(endHour)
                .AddMinutes(endMinute);

            if (end <= start)
            {
                MessageBox.Show("Дата окончания должна быть больше даты начала");
                return;
            }

            period.Name = name;
            period.StartDateTime = start;
            period.EndDateTime = end;

            RefreshPeriodsList();
            RefreshPeriodEditor();

            StatusTextBlock.Text = "Период обновлён";
            ScheduleAutoSave();
        }

        private void TimeTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void DeleteImageRowButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SlideItem slide)
            {
                RemoveSlide(slide);
            }
        }

        private void TimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            string text = new string(textBox.Text.Where(char.IsDigit).ToArray());

            if (text.Length > 2)
                text = text.Substring(0, 2);

            if (textBox.Text != text)
            {
                int caret = textBox.CaretIndex;
                textBox.Text = text;
                textBox.CaretIndex = Math.Min(caret, textBox.Text.Length);
            }
        }

        private void InitializeDefaultPeriods()
        {
            if (periods.Count == 0)
            {
                var defaultPeriod = new PlaylistPeriod
                {
                    Name = DefaultPeriodName,
                    StartDateTime = DateTime.Today,
                    EndDateTime = DateTime.Today.AddDays(1)
                };

                periods.Add(defaultPeriod);
                selectedPeriod = defaultPeriod;
            }
        }

        private void OpenLastPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string lastPath = GetLastPlaylistPath();

                if (string.IsNullOrWhiteSpace(lastPath) || !File.Exists(lastPath))
                {
                    MessageBox.Show("Последний плейлист не найден.");
                    return;
                }

                LoadPlaylistFromFile(lastPath, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия последнего плейлиста:\n{ex.Message}");
            }
        }

        private void LoadPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Загрузить плейлист",
                Filter = "JSON Playlist|*.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                LoadPlaylistFromFile(dialog.FileName, true);
                MessageBox.Show("Плейлист успешно загружен.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки:\n{ex.Message}");
            }
        }

        private void RefreshPlaylistHeader()
        {
            string name = "Новый плейлист";

            if (PlaylistNameTextBox != null && !string.IsNullOrWhiteSpace(PlaylistNameTextBox.Text))
                name = PlaylistNameTextBox.Text.Trim();

            if (PlaylistHeaderTextBlock != null)
                PlaylistHeaderTextBlock.Text = $"ПЛЕЙЛИСТ: {name.ToUpper()}";
        }
        private void CommitAllEdits()
        {
            Keyboard.ClearFocus();
            ImagesDataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            ImagesDataGrid?.CommitEdit(DataGridEditingUnit.Row, true);
        }
        private void SavePlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            CommitAllEdits();
            string filePath = PlaylistPathTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                SaveFileDialog dialog = new SaveFileDialog
                {
                    Title = "Сохранить плейлист",
                    Filter = "JSON Playlist|*.json",
                    FileName = "playlist.json"


                };

                if (dialog.ShowDialog() != true)
                    return;

                filePath = dialog.FileName;
                PlaylistPathTextBox.Text = filePath;
            }

            try
            {
                var data = BuildPlaylistData();
                PlaylistFileService.Save(filePath, data);

                SaveLastPlaylistPath(filePath);

                RefreshPlaylistHeader();
                UpdateStatus("Плейлист сохранён");
                MessageBox.Show("Плейлист успешно сохранён.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения:\n{ex.Message}");
            }
        }

        private void PlaylistNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshPlaylistHeader();
        }

        private void OpenSliderButton_Click(object sender, RoutedEventArgs e)
        {
            if (periods == null || periods.Count == 0)
            {
                MessageBox.Show("Нет периодов для показа");
                return;
            }

            sliderWindow?.Close();
            sliderWindow = null;
            sliderWindow = new SliderWindow(periods, PlaylistPathTextBox.Text);
            sliderWindow.Closed += SliderWindow_Closed;
            sliderWindow.Show();

            UpdateStatus("Слайдер запущен");
        }

        private void SliderWindow_Closed(object? sender, EventArgs e)
        {
            sliderWindow = null;
        }

        // =========================
        // ПЕРИОДЫ
        // =========================

        private void DaysListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DaysListBox.SelectedItem is PlaylistPeriod period)
            {
                selectedPeriod = period;
                RefreshPeriodEditor();
                RefreshMediaGrid();
                UpdateStatus();

            }
        }
        private void AddDayGroupButton_Click(object sender, RoutedEventArgs e)
        {
            DateTime startDate;

            if (periods.Count > 0)
            {
                DateTime latestEndDate = periods.Max(p => p.EndDateTime);

                startDate = latestEndDate.Date.AddDays(1);
            }
            else
            {
                startDate = DateTime.Today;
            }

            var newPeriod = new PlaylistPeriod
            {
                Name = $"Период {periods.Count + 1}",
                StartDateTime = startDate,
                EndDateTime = startDate.AddDays(1).AddSeconds(-1)
            };

            periods.Add(newPeriod);
            selectedPeriod = newPeriod;

            RefreshPeriodUi();
            UpdateStatus("Добавлен новый период");
            ScheduleAutoSave();
        }
        private void ImagesDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column.Header?.ToString() == "ЭФФЕКТ")
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (e.Column.GetCellContent(e.Row)?.Parent is DataGridCell cell)
                    {
                        var comboBox = FindVisualChild<ComboBox>(cell);
                        if (comboBox != null)
                        {
                            comboBox.Focus();
                            comboBox.IsDropDownOpen = true;
                        }
                    }
                }), DispatcherPriority.Background);
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is T result)
                    return result;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        // =========================
        // ФОТО
        // =========================

        private void AddImagesButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPeriod is not { } period)
            {
                MessageBox.Show(SelectPeriodFirstMessage);
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Выберите изображения",
                Filter = "Медиафайлы|*.jpg;*.jpeg;*.png;*.bmp;*.mp4;*.avi;*.mov;*.mkv;*.wmv|Изображения|*.jpg;*.jpeg;*.png;*.bmp|Видео|*.mp4;*.avi;*.mov;*.mkv;*.wmv",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    AddMediaToSelectedPeriod(file);
                }

                RefreshMediaGrid();
                UpdateStatus("Медиа добавлены");
                ScheduleAutoSave();
            }
        }

        private void RemoveSelectedImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (ImagesDataGrid.SelectedItem is SlideItem slide)
            {
                RemoveSlide(slide);
            }
        }

        private void ClearDragHighlight()
        {
            if (_lastHighlightedRow != null)
            {
                _lastHighlightedRow.ClearValue(DataGridRow.BackgroundProperty);
                _lastHighlightedRow = null;
            }
        }

        private void ImagesDataGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.Row.Item is SlideItem slide)
            {
                if (slide.Type == MediaType.Image)
                {
                    if (slide.DurationSeconds <= 0)
                        slide.DurationSeconds = DefaultSlideDurationSeconds;
                }
                else if (slide.Type == MediaType.Video)
                {
                    if (slide.PlayFullVideo)
                    {
                        slide.DurationSeconds = 0;
                    }
                    else
                    {
                        if (slide.DurationSeconds < 0)
                            slide.DurationSeconds = 0;
                    }
                }

                UpdateStatus("Параметры элемента обновлены");
                ScheduleAutoSave();
            }
        }

        private void DurationEditingTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }


        private void ImagesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImagesDataGrid.SelectedItem is SlideItem selectedSlide)
            {
                SelectedImagePathTextBox.Text = selectedSlide.Path;
                DurationTextBox.Text = selectedSlide.DurationSeconds.ToString();

                foreach (ComboBoxItem item in TransitionEffectComboBox.Items)
                {
                    if ((item.Content?.ToString() ?? "") == selectedSlide.TransitionEffect)
                    {
                        TransitionEffectComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }



        // =========================
        // DRAG & DROP
        // =========================

        private void RemoveSlide(SlideItem slide)
        {
            if (selectedPeriod is not { } period)
                return;

            period.Slides.Remove(slide);

            RefreshMediaGrid();
            ClearSelectedImageEditor();
            UpdateStatus("Изображение удалено");
            ScheduleAutoSave();
        }

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (selectedPeriod is not { })
            {
                MessageBox.Show(SelectPeriodFirstMessage);
                return;
            }

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (var file in files)
            {
                string extension = Path.GetExtension(file).ToLower();

                if (SupportedImageExtensions.Contains(extension) || SupportedVideoExtensions.Contains(extension))
                {
                    AddMediaToSelectedPeriod(file);
                }
            }

            RefreshMediaGrid();
            UpdateStatus("Медиа добавлены через Drag & Drop");
            ScheduleAutoSave();
        }



        private void AddMediaToSelectedPeriod(string file)
        {
            if (selectedPeriod is not { } period)
                return;

            string ext = Path.GetExtension(file).ToLower();

            MediaType type;

            if (SupportedImageExtensions.Contains(ext))
                type = MediaType.Image;
            else if (SupportedVideoExtensions.Contains(ext))
                type = MediaType.Video;
            else
                return;

            period.Slides.Add(new SlideItem
            {
                FileName = Path.GetFileName(file),
                Path = file,
                Type = type,
                DurationSeconds = type == MediaType.Video ? 0 : DefaultSlideDurationSeconds,
                TransitionEffect = DefaultTransitionEffect,
                PlayFullVideo = type == MediaType.Video,
                StartSeconds = 0,
                EndSeconds = 0
            });
        }


        private static readonly HashSet<string> SupportedImageExtensions = new()
{
    ".jpg",
    ".jpeg",
    ".png",
    ".bmp"
};
        private static readonly HashSet<string> SupportedVideoExtensions = new()
{
    ".mp4",
    ".avi",
    ".mov",
    ".mkv",
    ".wmv"
};
        private void ApplyImageSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (ImagesDataGrid.SelectedItem is not SlideItem selectedSlide)
            {
                MessageBox.Show(SelectImageFirstMessage);
                return;
            }

            if (selectedSlide.Type == MediaType.Image)
            {
                if (!int.TryParse(DurationTextBox.Text, out int imageDuration) || imageDuration <= 0)
                {
                    MessageBox.Show("Введите корректную длительность в секундах");
                    return;
                }

                selectedSlide.DurationSeconds = imageDuration;
            }
            else if (selectedSlide.Type == MediaType.Video)
            {
                if (selectedSlide.PlayFullVideo)
                {
                    selectedSlide.DurationSeconds = 0;
                }
                else
                {
                    if (!int.TryParse(DurationTextBox.Text, out int videoDuration) || videoDuration < 0)
                    {
                        MessageBox.Show("Введите корректную длительность в секундах");
                        return;
                    }

                    selectedSlide.DurationSeconds = videoDuration;
                }
            }

            if (TransitionEffectComboBox.SelectedItem is ComboBoxItem comboItem)
            {
                selectedSlide.TransitionEffect = comboItem.Content?.ToString() ?? DefaultTransitionEffect;
            }

            RefreshMediaGrid();
            ImagesDataGrid.SelectedItem = selectedSlide;
            UpdateStatus("Настройки элемента применены");
            ScheduleAutoSave();
        }

        // =========================
        // ПЛЕЙЛИСТ
        // =========================

        private void ChoosePlaylistPathButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Выберите путь для плейлиста",
                Filter = "JSON Playlist|*.json",

            };

            if (dialog.ShowDialog() == true)
            {
                PlaylistPathTextBox.Text = dialog.FileName;
                UpdateStatus("Путь плейлиста выбран");
            }
        }

        private void RefreshPeriodUi()
        {
            RefreshPeriodsList();
            RefreshPeriodEditor();
            RefreshMediaGrid();
        }

        private void AutoSaveCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Автосохранение включено");
        }

        private void AutoSaveCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Автосохранение выключено");
        }

        private void ApplyAutoSaveButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Настройка автосохранения будет позже");
        }

        // =========================
        // ВСПОМОГАТЕЛЬНОЕ
        // =========================

        private void RefreshPeriodsList()
        {
            DaysListBox.ItemsSource = null;
            DaysListBox.ItemsSource = periods;

            if (selectedPeriod != null)
            {
                DaysListBox.SelectedItem = selectedPeriod;
            }
        }

        private void RefreshPeriodEditor()
        {
            if (selectedPeriod is not { } period)
            {
                if (PeriodNameTextBox != null)
                    PeriodNameTextBox.Text = "";

                if (StartDatePicker != null)
                    StartDatePicker.SelectedDate = null;

                if (EndDatePicker != null)
                    EndDatePicker.SelectedDate = null;

                if (StartHourTextBox != null)
                    StartHourTextBox.Text = "00";

                if (StartMinuteTextBox != null)
                    StartMinuteTextBox.Text = "00";

                if (EndHourTextBox != null)
                    EndHourTextBox.Text = "00";

                if (EndMinuteTextBox != null)
                    EndMinuteTextBox.Text = "00";

                if (CurrentDayInfoTextBlock != null)
                    CurrentDayInfoTextBlock.Text = "Период: не выбран";

                return;
            }

            if (PeriodNameTextBox != null)
                PeriodNameTextBox.Text = period.Name;

            if (StartDatePicker != null)
                StartDatePicker.SelectedDate = period.StartDateTime.Date;

            if (EndDatePicker != null)
                EndDatePicker.SelectedDate = period.EndDateTime.Date;

            if (StartHourTextBox != null)
                StartHourTextBox.Text = period.StartDateTime.Hour.ToString("00");

            if (StartMinuteTextBox != null)
                StartMinuteTextBox.Text = period.StartDateTime.Minute.ToString("00");

            if (EndHourTextBox != null)
                EndHourTextBox.Text = period.EndDateTime.Hour.ToString("00");

            if (EndMinuteTextBox != null)
                EndMinuteTextBox.Text = period.EndDateTime.Minute.ToString("00");

            if (CurrentDayInfoTextBlock != null)
            {
                CurrentDayInfoTextBlock.Text =
                    $"Период: {period.Name}\n" +
                    $"С: {period.StartDateTime:dd.MM.yyyy HH:mm}\n" +
                    $"По: {period.EndDateTime:dd.MM.yyyy HH:mm}";
            }
        }

        private void RefreshMediaGrid()
        {
            ImagesDataGrid.ItemsSource = null;

            if (selectedPeriod != null)
            {
                ImagesDataGrid.ItemsSource = selectedPeriod.Slides;
                EmptyDropHintTextBlock.Visibility = selectedPeriod.Slides.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else
            {
                EmptyDropHintTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void ClearSelectedImageEditor()
        {
            SelectedImagePathTextBox.Text = "";
            DurationTextBox.Text = "5";
            TransitionEffectComboBox.SelectedIndex = 0;
        }

        private void UpdateStatus(string? customText = null)
        {
            int count = selectedPeriod?.Slides.Count ?? 0;
            string periodName = selectedPeriod?.Name ?? "не выбран";

            StatusTextBlock.Text = customText ?? $"Статус: изображений в периоде \"{periodName}\" — {count}";

            RefreshPeriodEditor();
        }

        // TODO: использовать для автостарта активного периода в слайдере
        private PlaylistPeriod? GetActivePeriod()
        {
            DateTime now = DateTime.Now;

            return periods.FirstOrDefault(p =>
                now >= p.StartDateTime && now < p.EndDateTime);
        }
    }
}