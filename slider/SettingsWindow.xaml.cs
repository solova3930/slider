using slider.Models;
using slider.Services;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Windows.Interop;
namespace slider
{
    public partial class SettingsWindow : Window
    {
        private readonly DispatcherTimer playlistStreamWatchdogTimer = new();
        private readonly DispatcherTimer playlistScheduleTimer = new();
        private bool playlistStreamShouldBeRunning = false;
        private readonly PlaylistRenderService playlistRenderService = new();
        public PlaylistData SettingsData { get; private set; }

        private List<SlideItem> currentStreamSlides = new();
        private List<StreamSlideState> currentStreamSlideStates = new();
        private StreamSettings? currentStreamSettings = null;
        private string currentStreamFfmpegPath = "ffmpeg.exe";
        private DateTime? exitCodeWaitStartedAt = null;
        private static readonly TimeSpan ExitCodeWaitTimeout = TimeSpan.FromSeconds(3);

        private readonly record struct StreamSlideState(
            string Path,
            MediaType Type,
            int DurationSeconds,
            string TransitionEffect,
            bool PlayFullVideo,
            double StartSeconds,
            double EndSeconds);

        private void CleanupFfmpeg()
        {
            playlistStreamShouldBeRunning = false;
            playlistStreamWatchdogTimer.Stop();
            playlistScheduleTimer.Stop();
            playlistRenderService.StopStreaming();

            currentStreamSlides.Clear();
            currentStreamSlideStates.Clear();
            currentStreamSettings = null;
            exitCodeWaitStartedAt = null;

        }

        protected override void OnClosed(EventArgs e)
        {
            CleanupFfmpeg();
            base.OnClosed(e);
        }

        private void StopPlaylistStreamButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GdigrabStreamService.Stop();
                MessageBox.Show("Production GDIGRAB-стрим остановлен.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка остановки стрима:\n{ex.Message}");
            }
        }

        public SettingsWindow(PlaylistData data)
        {
            InitializeComponent();
            Loaded += SliderWindow_Loaded;
            SettingsData = data;

            LoadDataToForm();

            playlistStreamWatchdogTimer.Interval = TimeSpan.FromSeconds(1);
            playlistStreamWatchdogTimer.Tick += PlaylistStreamWatchdogTimer_Tick;

            playlistScheduleTimer.Interval = TimeSpan.FromSeconds(10);
            playlistScheduleTimer.Tick += PlaylistScheduleTimer_Tick;
        }

        private void PlaylistScheduleTimer_Tick(object? sender, EventArgs e)
        {
            if (!playlistStreamShouldBeRunning)
                return;

            List<SlideItem> activeSlides = GetActiveSlidesForFfmpeg();
            List<StreamSlideState> activeSlideStates = GetStreamSlideStates(activeSlides);

            if (currentStreamSlideStates.SequenceEqual(activeSlideStates))
                return;

            playlistRenderService.StopStreaming();
            currentStreamSlides = activeSlides;
            currentStreamSlideStates = activeSlideStates;

            // Keep schedule monitoring enabled. A later active period can then
            // resume the stream automatically without user intervention.
            if (currentStreamSlides.Count == 0 || currentStreamSettings == null)
                return;

            try
            {
                exitCodeWaitStartedAt = null;
                playlistRenderService.StartStreaming(
                    currentStreamSlides,
                    currentStreamSettings,
                    currentStreamFfmpegPath);
            }
            catch (Exception ex)
            {
                CleanupFfmpeg();
                MessageBox.Show($"Ошибка обновления стрима по расписанию:\n{ex.Message}");
            }
        }

        private void PlaylistStreamWatchdogTimer_Tick(object? sender, EventArgs e)
        {
            if (!playlistStreamShouldBeRunning)
                return;

            if (playlistRenderService.IsStreaming())
            {
                exitCodeWaitStartedAt = null;
                return;
            }

            List<SlideItem> activeSlides = GetActiveSlidesForFfmpeg();
            List<StreamSlideState> activeSlideStates = GetStreamSlideStates(activeSlides);
            bool scheduleChanged = !currentStreamSlideStates.SequenceEqual(activeSlideStates);

            if (scheduleChanged)
            {
                currentStreamSlides = activeSlides;
                currentStreamSlideStates = activeSlideStates;
            }

            if (currentStreamSlides.Count == 0 || currentStreamSettings == null)
                return;

            bool loopEnabled = LoopPlaylistCheckBox.IsChecked.GetValueOrDefault();
            int? exitCode = playlistRenderService.LastStreamingExitCode;

            if (!scheduleChanged && !exitCode.HasValue)
            {
                DateTime now = DateTime.UtcNow;

                if (!exitCodeWaitStartedAt.HasValue)
                {
                    exitCodeWaitStartedAt = now;
                    return;
                }

                if (now - exitCodeWaitStartedAt.Value < ExitCodeWaitTimeout)
                    return;
            }
            else
            {
                exitCodeWaitStartedAt = null;
            }

            bool unexpectedExit = !exitCode.HasValue || exitCode.Value != 0;

            // A changed schedule must start immediately. Otherwise, repeat a
            // normally completed playlist only when Loop is enabled, while an
            // abnormal FFmpeg exit is always recovered.
            if (!scheduleChanged && !loopEnabled && !unexpectedExit)
                return;

            try
            {
                exitCodeWaitStartedAt = null;
                playlistRenderService.StartStreaming(
                    currentStreamSlides,
                    currentStreamSettings,
                    currentStreamFfmpegPath);
            }
            catch (Exception ex)
            {
                CleanupFfmpeg();
                MessageBox.Show($"Ошибка восстановления FFmpeg-стрима:\n{ex.Message}");
            }
        }


        private void StartPlaylistStreamButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (GdigrabStreamService.IsActive)
                {
                    MessageBox.Show("Production GDIGRAB-стрим уже запущен.");
                    return;
                }

                SliderWindow? sliderWindow = Application.Current.Windows
                    .OfType<SliderWindow>()
                    .FirstOrDefault(window => window.IsLoaded);

                if (sliderWindow == null)
                {
                    MessageBox.Show("Сначала открой окно слайдера.");
                    return;
                }

                IntPtr hwnd = new WindowInteropHelper(sliderWindow).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    MessageBox.Show("Не удалось получить HWND окна слайдера.");
                    return;
                }

                string outputUrl = OutputUrlTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(outputUrl))
                {
                    MessageBox.Show("Укажи URL потока.");
                    return;
                }

                int.TryParse(BitrateTextBox.Text, out int bitrate);
                int.TryParse(FpsTextBox.Text, out int fps);
                if (bitrate <= 0) bitrate = 4000;
                if (fps <= 0) fps = 25;

                string codec = "libx264";
                if (CodecComboBox.SelectedItem is ComboBoxItem codecItem)
                {
                    string selectedCodec = codecItem.Content?.ToString() ?? "H264";
                    if (selectedCodec == "HEVC") codec = "libx265";
                    else if (selectedCodec == "MPEG2") codec = "mpeg2video";
                }

                string preset = "veryfast";
                if (PresetComboBox.SelectedItem is ComboBoxItem presetItem)
                    preset = presetItem.Content?.ToString() ?? "veryfast";

                var settings = new StreamSettings
                {
                    FfmpegPath = "ffmpeg.exe",
                    OutputUrl = outputUrl,
                    VideoCodec = codec,
                    Format = "mpegts",
                    Fps = fps,
                    BitrateKbps = bitrate,
                    Preset = preset
                };

                string command = GdigrabStreamService.Start(hwnd, settings);
                MessageBox.Show($"Production GDIGRAB-стрим запущен:\n{outputUrl}\n\n{command}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска production GDIGRAB-стрима:\n{ex.Message}");
            }
        }

        // Deprecated legacy fallback. Kept for a possible explicit rollback only;
        // the production Start button uses GdigrabStreamService exclusively.
        [Obsolete("Legacy fallback only. Production streaming uses GdigrabStreamService.")]
        private void StartLegacyPlaylistStream()
        {
            try
            {
                var activeSlides = GetActiveSlidesForFfmpeg();

                if (activeSlides.Count == 0)
                {
                    MessageBox.Show("Нет активного периода или в нём нет медиа.");
                    return;
                }

                string outputUrl = OutputUrlTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(outputUrl))
                {
                    MessageBox.Show("Укажи URL потока.");
                    return;
                }

                int bitrate = 4000;
                int fps = 25;
                int width = 1280;
                int height = 720;

                int.TryParse(BitrateTextBox.Text, out bitrate);
                int.TryParse(FpsTextBox.Text, out fps);
                int.TryParse(WidthTextBox.Text, out width);
                int.TryParse(HeightTextBox.Text, out height);

                if (bitrate <= 0) bitrate = 4000;
                if (fps <= 0) fps = 25;
                if (width <= 0) width = 1280;
                if (height <= 0) height = 720;

                string codec = "libx264";
                if (CodecComboBox.SelectedItem is ComboBoxItem codecItem)
                {
                    string selectedCodec = codecItem.Content?.ToString() ?? "H264";

                    if (selectedCodec == "HEVC")
                        codec = "libx265";
                    else if (selectedCodec == "MPEG2")
                        codec = "mpeg2video";
                }

                string preset = "veryfast";
                if (PresetComboBox.SelectedItem is ComboBoxItem presetItem)
                {
                    preset = presetItem.Content?.ToString() ?? "veryfast";
                }

                var settings = new StreamSettings
                {
                    FfmpegPath = "ffmpeg.exe",
                    OutputUrl = outputUrl,
                    VideoCodec = codec,
                    Format = "mpegts",
                    Width = width,
                    Height = height,
                    Fps = fps,
                    BitrateKbps = bitrate,
                    Preset = preset
                };

                currentStreamSlides = activeSlides.ToList();
                currentStreamSlideStates = GetStreamSlideStates(currentStreamSlides);
                currentStreamSettings = settings;
                currentStreamFfmpegPath = settings.FfmpegPath;

                exitCodeWaitStartedAt = null;
                playlistRenderService.StartStreaming(currentStreamSlides, currentStreamSettings, currentStreamFfmpegPath);

                playlistStreamShouldBeRunning = true;
                playlistStreamWatchdogTimer.Start();
                playlistScheduleTimer.Start();

                MessageBox.Show($"Стрим плейлиста запущен:\n{outputUrl}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска стрима плейлиста:\n{ex.Message}");
            }
        }

        private List<SlideItem> GetActiveSlidesForFfmpeg()
        {
            DateTime now = DateTime.Now;

            return SettingsData.Periods?
                .Where(p => p.IsActiveAt(now))
                .OrderBy(p => p.StartDateTime)
                .SelectMany(p => p.Slides ?? new List<SlideItem>())
                .ToList()
                ?? new List<SlideItem>();
        }

        private static List<StreamSlideState> GetStreamSlideStates(IEnumerable<SlideItem> slides)
        {
            return slides
                .Select(slide => new StreamSlideState(
                    slide.Path,
                    slide.Type,
                    slide.DurationSeconds,
                    slide.TransitionEffect,
                    slide.PlayFullVideo,
                    slide.StartSeconds,
                    slide.EndSeconds))
                .ToList();
        }
        private void SliderWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Focus();
            Keyboard.Focus(this);
        }

        private void LoadDataToForm()
        {
            PlaylistNameTextBox.Text = SettingsData.PlaylistName;
            PlaylistPathTextBox.Text = SettingsData.PlaylistPath;
            AutoSaveCheckBox.IsChecked = SettingsData.AutoSaveEnabled;
            AutoSaveMinutesTextBox.Text = SettingsData.AutoSaveMinutes.ToString();

            EnableStreamingCheckBox.IsChecked = SettingsData.StreamSettings.EnableStreaming;
            OutputUrlTextBox.Text = SettingsData.StreamSettings.OutputUrl;
            BitrateTextBox.Text = SettingsData.StreamSettings.BitrateKbps.ToString();
            FpsTextBox.Text = SettingsData.StreamSettings.Fps.ToString();
            WidthTextBox.Text = SettingsData.StreamSettings.Width.ToString();
            HeightTextBox.Text = SettingsData.StreamSettings.Height.ToString();
            LoopPlaylistCheckBox.IsChecked = SettingsData.StreamSettings.LoopPlaylist;

            SelectComboBoxItemByText(CodecComboBox, SettingsData.StreamSettings.Codec);
            SelectComboBoxItemByText(PresetComboBox, SettingsData.StreamSettings.Preset);
        }

        private void SelectComboBoxItemByText(ComboBox comboBox, string text)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if ((item.Content?.ToString() ?? "") == text)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(AutoSaveMinutesTextBox.Text, out int autoSaveMinutes))
                autoSaveMinutes = 5;

            if (!int.TryParse(BitrateTextBox.Text, out int bitrate))
                bitrate = 4000;

            if (!int.TryParse(FpsTextBox.Text, out int fps))
                fps = 25;

            if (!int.TryParse(WidthTextBox.Text, out int width))
                width = 1920;

            if (!int.TryParse(HeightTextBox.Text, out int height))
                height = 1080;

            SettingsData.PlaylistName = PlaylistNameTextBox.Text.Trim();
            SettingsData.PlaylistPath = PlaylistPathTextBox.Text.Trim();
            SettingsData.AutoSaveEnabled = AutoSaveCheckBox.IsChecked == true;
            SettingsData.AutoSaveMinutes = autoSaveMinutes;

            SettingsData.StreamSettings.EnableStreaming = EnableStreamingCheckBox.IsChecked == true;
            SettingsData.StreamSettings.OutputUrl = OutputUrlTextBox.Text.Trim();
            SettingsData.StreamSettings.BitrateKbps = bitrate;
            SettingsData.StreamSettings.Fps = fps;
            SettingsData.StreamSettings.Width = width;
            SettingsData.StreamSettings.Height = height;
            SettingsData.StreamSettings.LoopPlaylist = LoopPlaylistCheckBox.IsChecked == true;

            if (CodecComboBox.SelectedItem is ComboBoxItem codecItem)
                SettingsData.StreamSettings.Codec = codecItem.Content?.ToString() ?? "H264";

            if (PresetComboBox.SelectedItem is ComboBoxItem presetItem)
                SettingsData.StreamSettings.Preset = presetItem.Content?.ToString() ?? "medium";

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
