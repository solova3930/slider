using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.IO;
using slider.Services;
using slider.Models;


namespace slider
{
    public partial class MainWindow : Window
    {
        private List<PlaylistPeriod> periods = new List<PlaylistPeriod>();
        private PlaylistPeriod? selectedPeriod = null;
        private Point _dragStartPoint;
        private SlideItem? _draggedSlideItem;
        private DispatcherTimer? autoSaveDelayTimer;
        private bool isLoadingPlaylist = false;
        private string playlistFilePath = "";
        private DateTime lastFileWriteTime = DateTime.MinValue;
        private SliderWindow? sliderWindow = null;
        public MainWindow()
        {
            InitializeComponent();
            InitializeAutoSaveDelayTimer();
            InitializeDefaultPeriods();
            RefreshPeriodsList();
            RefreshImagesGrid();
            UpdateStatus();

        }

        // =========================
        // ИНИЦИАЛИЗАЦИЯ
        // =========================

        private string currentPlaylistFilePath = "";
        private readonly string lastPlaylistInfoFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_playlist.txt");
        private PlaylistData BuildPlaylistData()
        {
            int autoSaveMinutes = 5;
            int.TryParse(AutoSaveMinutesTextBox.Text, out autoSaveMinutes);
            if (autoSaveMinutes <= 0)
                autoSaveMinutes = 5;

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
                newName = "Новый период";

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

            RefreshPeriodsList();
            RefreshPeriodEditor();
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

                currentPlaylistFilePath = filePath;
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

                RefreshPeriodsList();
                RefreshPeriodEditor();
                RefreshImagesGrid();
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
            _dragStartPoint = e.GetPosition(null);

            if (sender is DataGrid dataGrid)
            {
                var row = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
                if (row?.Item is SlideItem slideItem)
                {
                    _draggedSlideItem = slideItem;
                }
                else
                {
                    _draggedSlideItem = null;
                }
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

            if (_lastHighlightedRow != null)
            {
                _lastHighlightedRow.Background = System.Windows.Media.Brushes.Transparent;
                _lastHighlightedRow = null;
            }

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
            if (_lastHighlightedRow != null)
            {
                _lastHighlightedRow.ClearValue(DataGridRow.BackgroundProperty);
                _lastHighlightedRow = null;
            }

            if (selectedPeriod == null)
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

            int oldIndex = selectedPeriod.Slides.IndexOf(droppedData);
            int newIndex = selectedPeriod.Slides.IndexOf(targetItem);

            if (oldIndex < 0 || newIndex < 0)
                return;

            selectedPeriod.Slides.RemoveAt(oldIndex);
            selectedPeriod.Slides.Insert(newIndex, droppedData);

            RefreshImagesGrid();
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
            if (_lastHighlightedRow != null)
            {
                _lastHighlightedRow.ClearValue(DataGridRow.BackgroundProperty);
                _lastHighlightedRow = null;
            }
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
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    periods.Remove(period);
                    selectedPeriod = periods.FirstOrDefault();

                    RefreshPeriodsList();
                    RefreshPeriodEditor();
                    RefreshImagesGrid();
                    ClearSelectedImageEditor();

                    UpdateStatus("Период удалён");
                    ScheduleAutoSave();
                }
            }
        }

        private void ApplyPeriodSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPeriod == null)
            {
                MessageBox.Show("Сначала выбери период");
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

            selectedPeriod.Name = name;
            selectedPeriod.StartDateTime = start;
            selectedPeriod.EndDateTime = end;

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
            if (selectedPeriod == null)
                return;

            if ((sender as FrameworkElement)?.DataContext is SlideItem selectedSlide)
            {
                selectedPeriod.Slides.Remove(selectedSlide);
                RefreshImagesGrid();
                ClearSelectedImageEditor();
                UpdateStatus("Изображение удалено");
                ScheduleAutoSave();
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
                    Name = "Новый период",
                    StartDateTime = DateTime.Today,
                    EndDateTime = DateTime.Today.AddDays(1)
                };

                periods.Add(defaultPeriod);
                selectedPeriod = defaultPeriod;
            }
        }

        // =========================
        // ВЕРХНЯЯ ПАНЕЛЬ
        // =========================

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

                var data = PlaylistFileService.Load(lastPath);

                currentPlaylistFilePath = lastPath;
                PlaylistPathTextBox.Text = lastPath;

                ApplyPlaylistData(data);

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
                var data = PlaylistFileService.Load(dialog.FileName);

                currentPlaylistFilePath = dialog.FileName;
                PlaylistPathTextBox.Text = dialog.FileName;

                ApplyPlaylistData(data);
                SaveLastPlaylistPath(dialog.FileName);

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
            try
            {
                Keyboard.ClearFocus();

                if (ImagesDataGrid != null)
                {
                    ImagesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                    ImagesDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
                }
            }
            catch
            {
            }
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

                currentPlaylistFilePath = filePath;
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

            if (sliderWindow != null)
            {
                try
                {
                    sliderWindow.Close();
                }
                catch
                {
                }

                sliderWindow = null;
            }

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
                RefreshImagesGrid();
                UpdateStatus();
                UpdateStatus();
            }
        }

        private void AddDayGroupButton_Click(object sender, RoutedEventArgs e)
        {
            DateTime now = DateTime.Now;

            var newPeriod = new PlaylistPeriod
            {
                Name = $"Период {periods.Count + 1}",
                StartDateTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0),
                EndDateTime = new DateTime(now.Year, now.Month, now.Day, 23, 59, 59)
            };

            periods.Add(newPeriod);
            selectedPeriod = newPeriod;

            RefreshPeriodsList();
            RefreshPeriodEditor();
            RefreshImagesGrid();
            UpdateStatus("Добавлен новый период");
            ScheduleAutoSave();
        }

        // =========================
        // ФОТО
        // =========================

        private void AddImagesButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPeriod == null)
            {
                MessageBox.Show("Сначала выбери период");
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Выберите изображения",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    selectedPeriod.Slides.Add(new SlideItem
                    {
                        FileName = System.IO.Path.GetFileName(file),
                        ImagePath = file,
                        DurationSeconds = 5,
                        TransitionEffect = "Затухание"
                    });
                }

                RefreshImagesGrid();
                UpdateStatus("Изображения добавлены");
                ScheduleAutoSave();
            }
        }

        private void RemoveSelectedImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPeriod == null)
                return;

            if (ImagesDataGrid.SelectedItem is SlideItem selectedSlide)
            {
                selectedPeriod.Slides.Remove(selectedSlide);
                RefreshImagesGrid();
                ClearSelectedImageEditor();
                UpdateStatus("Изображение удалено");
                ScheduleAutoSave();
            }
        }

        private void ImagesDataGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.Row.Item is SlideItem slide)
            {
                if (slide.DurationSeconds <= 0)
                    slide.DurationSeconds = 5;

                UpdateStatus("Длительность обновлена");
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
                SelectedImagePathTextBox.Text = selectedSlide.ImagePath;
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

        private void ImagesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ImagesDataGrid.SelectedItem is SlideItem selectedSlide)
            {
                
            }
        }

        // =========================
        // DRAG & DROP
        // =========================

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
            if (selectedPeriod == null)
            {
                MessageBox.Show("Сначала выбери период");
                return;
            }

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (var file in files)
            {
                string extension = System.IO.Path.GetExtension(file).ToLower();

                if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".bmp")
                {
                    selectedPeriod.Slides.Add(new SlideItem
                    {
                        FileName = System.IO.Path.GetFileName(file),
                        ImagePath = file,
                        DurationSeconds = 5,
                        TransitionEffect = "Затухание"
                    });
                }
            }

            RefreshImagesGrid();
            UpdateStatus("Изображения добавлены через Drag & Drop");
            ScheduleAutoSave();
        }

        // =========================
        // НАСТРОЙКИ ИЗОБРАЖЕНИЯ
        // =========================

        private void ApplyImageSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (ImagesDataGrid.SelectedItem is not SlideItem selectedSlide)
            {
                MessageBox.Show("Сначала выбери изображение");
                return;
            }

            if (!int.TryParse(DurationTextBox.Text, out int duration) || duration <= 0)
            {
                MessageBox.Show("Введите корректную длительность в секундах");
                return;
            }

            selectedSlide.DurationSeconds = duration;

            if (TransitionEffectComboBox.SelectedItem is ComboBoxItem comboItem)
            {
                selectedSlide.TransitionEffect = comboItem.Content?.ToString() ?? "Затухание";
            }

            RefreshImagesGrid();
            ImagesDataGrid.SelectedItem = selectedSlide;
            UpdateStatus("Настройки изображения применены");
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
                currentPlaylistFilePath = dialog.FileName;
                UpdateStatus("Путь плейлиста выбран");
            }
        }

        // =========================
        // АВТОСОХРАНЕНИЕ
        // =========================

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
            if (selectedPeriod == null)
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
                PeriodNameTextBox.Text = selectedPeriod.Name;

            if (StartDatePicker != null)
                StartDatePicker.SelectedDate = selectedPeriod.StartDateTime.Date;

            if (EndDatePicker != null)
                EndDatePicker.SelectedDate = selectedPeriod.EndDateTime.Date;

            if (StartHourTextBox != null)
                StartHourTextBox.Text = selectedPeriod.StartDateTime.Hour.ToString("00");

            if (StartMinuteTextBox != null)
                StartMinuteTextBox.Text = selectedPeriod.StartDateTime.Minute.ToString("00");

            if (EndHourTextBox != null)
                EndHourTextBox.Text = selectedPeriod.EndDateTime.Hour.ToString("00");

            if (EndMinuteTextBox != null)
                EndMinuteTextBox.Text = selectedPeriod.EndDateTime.Minute.ToString("00");

            if (CurrentDayInfoTextBlock != null)
            {
                CurrentDayInfoTextBlock.Text =
                    $"Период: {selectedPeriod.Name}\n" +
                    $"С: {selectedPeriod.StartDateTime:dd.MM.yyyy HH:mm}\n" +
                    $"По: {selectedPeriod.EndDateTime:dd.MM.yyyy HH:mm}";
            }
        }

        private void RefreshImagesGrid()
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

        private PlaylistPeriod? GetActivePeriod()
        {
            DateTime now = DateTime.Now;

            return periods.FirstOrDefault(p =>
                now >= p.StartDateTime && now < p.EndDateTime);
        }
    }
}