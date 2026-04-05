using Microsoft.Win32;
using slider.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using slider.Services;
using System.IO;

namespace slider
{
    public partial class MainWindow : Window
    {
        private List<PlaylistPeriod> periods = new List<PlaylistPeriod>();
        private PlaylistPeriod? selectedPeriod = null;

        public MainWindow()
        {
            InitializeComponent();
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
                PlaylistName = PlaylistNameTextBox.Text.Trim(),
                PlaylistPath = PlaylistPathTextBox.Text.Trim(),
                AutoSaveEnabled = AutoSaveCheckBox.IsChecked == true,
                AutoSaveMinutes = autoSaveMinutes,
                Periods = periods
            };
        }

        private void ApplyPlaylistData(PlaylistData data)
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

            PlaylistNameTextBox.Text = data.PlaylistName ?? "Новый плейлист";
            PlaylistPathTextBox.Text = data.PlaylistPath ?? "";
            AutoSaveCheckBox.IsChecked = data.AutoSaveEnabled;
            AutoSaveMinutesTextBox.Text = data.AutoSaveMinutes.ToString();

            RefreshPeriodsList();
            RefreshPeriodEditor();
            RefreshImagesGrid();
            ClearSelectedImageEditor();
            UpdateStatus("Плейлист загружен");
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

            if (!DateTime.TryParseExact(PeriodStartTextBox.Text, "dd.MM.yyyy HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime start))
            {
                MessageBox.Show("Неверная дата начала");
                return;
            }

            if (!DateTime.TryParseExact(PeriodEndTextBox.Text, "dd.MM.yyyy HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime end))
            {
                MessageBox.Show("Неверная дата конца");
                return;
            }

            if (end <= start)
            {
                MessageBox.Show("Дата окончания должна быть больше начала");
                return;
            }

            selectedPeriod.Name = name;
            selectedPeriod.StartDateTime = start;
            selectedPeriod.EndDateTime = end;

            RefreshPeriodsList();
            RefreshPeriodEditor();

            StatusTextBlock.Text = "Период обновлён";
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
            MessageBox.Show("Открытие последнего плейлиста пока не реализовано");
        }

        private void LoadPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Загрузка плейлиста пока не реализована");
        }

        private void SavePlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Сохранение плейлиста пока не реализовано");
        }

        private void OpenSliderButton_Click(object sender, RoutedEventArgs e)
        {
            var activePeriod = GetActivePeriod();

            if (activePeriod == null)
            {
                MessageBox.Show("Сейчас нет активного периода показа");
                return;
            }

            if (activePeriod.Slides.Count == 0)
            {
                MessageBox.Show("В активном периоде нет изображений");
                return;
            }

            SliderWindow slider = new SliderWindow(periods);
            slider.Show();
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
                    if (!selectedPeriod.Slides.Any(s => s.ImagePath == file))
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
                UpdateStatus("Изображения добавлены");
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
            }
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
                MessageBox.Show($"Выбрано изображение:\n{selectedSlide.FileName}");
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
                    if (!selectedPeriod.Slides.Any(s => s.ImagePath == file))
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
            }

            RefreshImagesGrid();
            UpdateStatus("Изображения добавлены через Drag & Drop");
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
        }

        // =========================
        // ПЛЕЙЛИСТ
        // =========================

        private void ChoosePlaylistPathButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Выбор пути для плейлиста сделаем позже");
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

                if (PeriodStartTextBox != null)
                    PeriodStartTextBox.Text = "";

                if (PeriodEndTextBox != null)
                    PeriodEndTextBox.Text = "";

                if (CurrentDayInfoTextBlock != null)
                    CurrentDayInfoTextBlock.Text = "Период: не выбран";

                return;
            }

            if (PeriodNameTextBox != null)
                PeriodNameTextBox.Text = selectedPeriod.Name;

            if (PeriodStartTextBox != null)
                PeriodStartTextBox.Text = selectedPeriod.StartDateTime.ToString("dd.MM.yyyy HH:mm");

            if (PeriodEndTextBox != null)
                PeriodEndTextBox.Text = selectedPeriod.EndDateTime.ToString("dd.MM.yyyy HH:mm");

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