using slider.Models;
using slider.Services;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Generic;
using System.Windows.Threading;
namespace slider
{
    public partial class SettingsWindow : Window
    {
        private readonly DispatcherTimer playlistStreamWatchdogTimer = new();
        private bool playlistStreamShouldBeRunning = false;
        private readonly FfmpegOutputService ffmpegOutputService = new();
        private readonly PlaylistRenderService playlistRenderService = new();
        public PlaylistData SettingsData { get; private set; }

        private readonly DispatcherTimer ffmpegPlaybackTimer = new();
        private int ffmpegCurrentSlideIndex = 0;
        private List<SlideItem> ffmpegSlides = new();
        private List<SlideItem> currentStreamSlides = new();
        private StreamSettings? currentStreamSettings = null;
        private string currentStreamFfmpegPath = "ffmpeg.exe";

        private void StopPlaylistStreamButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                playlistStreamShouldBeRunning = false;
                playlistStreamWatchdogTimer.Stop();

                currentStreamSlides.Clear();
                currentStreamSettings = null;

                playlistRenderService.StopStreaming();

                if (playlistRenderService.IsStreaming())
                {
                    MessageBox.Show("FFmpeg не остановился.");
                }
                else
                {
                    MessageBox.Show("Стрим плейлиста остановлен.");
                }
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

            ffmpegPlaybackTimer.Tick += FfmpegPlaybackTimer_Tick;

            playlistStreamWatchdogTimer.Interval = TimeSpan.FromSeconds(1);
            playlistStreamWatchdogTimer.Tick += PlaylistStreamWatchdogTimer_Tick;
        }

        private void PlaylistStreamWatchdogTimer_Tick(object? sender, EventArgs e)
        {
            if (!playlistStreamShouldBeRunning)
                return;

            if (!LoopPlaylistCheckBox.IsChecked.GetValueOrDefault())
                return;

            if (playlistRenderService.IsStreaming())
                return;

            if (currentStreamSlides == null || currentStreamSlides.Count == 0)
                return;

            if (currentStreamSettings == null)
                return;

            try
            {
                playlistRenderService.StartStreaming(
                    currentStreamSlides,
                    currentStreamSettings,
                    currentStreamFfmpegPath);
            }
            catch (Exception ex)
            {
                playlistStreamShouldBeRunning = false;
                playlistStreamWatchdogTimer.Stop();
                MessageBox.Show($"Ошибка перезапуска loop-стрима:\n{ex.Message}");
            }
        }


        private void StartPlaylistStreamButton_Click(object sender, RoutedEventArgs e)
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
                currentStreamSettings = settings;
                currentStreamFfmpegPath = settings.FfmpegPath;

                playlistRenderService.StartStreaming(currentStreamSlides, currentStreamSettings, currentStreamFfmpegPath);

                playlistStreamShouldBeRunning = true;
                playlistStreamWatchdogTimer.Start();

                MessageBox.Show($"Стрим плейлиста запущен:\n{outputUrl}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска стрима плейлиста:\n{ex.Message}");
            }
        }

        private void TestRenderPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var activeSlides = GetActiveSlidesForFfmpeg();

                if (activeSlides.Count == 0)
                {
                    MessageBox.Show("Нет активного периода или в нём нет медиа.");
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
                    else
                        codec = "libx264";
                }

                string preset = "veryfast";
                if (PresetComboBox.SelectedItem is ComboBoxItem presetItem)
                {
                    preset = presetItem.Content?.ToString() ?? "veryfast";
                }

                var settings = new StreamSettings
                {
                    FfmpegPath = "ffmpeg.exe",
                    VideoCodec = codec,
                    Width = width,
                    Height = height,
                    Fps = fps,
                    BitrateKbps = bitrate,
                    Preset = preset
                };

                string outputPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "playlist_render_test.mp4");

                playlistRenderService.RenderToFile(
                    activeSlides,
                    settings,
                    settings.FfmpegPath,
                    outputPath);

                MessageBox.Show($"Рендер завершён:\n{outputPath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка рендера плейлиста:\n{ex.Message}");
            }
        }

        private void TestFfmpegStopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ffmpegPlaybackTimer.Stop();
                ffmpegOutputService.Stop();

                ffmpegCurrentSlideIndex = 0;
                ffmpegSlides.Clear();

                MessageBox.Show("FFmpeg остановлен.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка остановки FFmpeg:\n{ex.Message}");
            }
        }

        private void TestFfmpegStartButton_Click(object sender, RoutedEventArgs e)
        {
            ffmpegPlaybackTimer.Stop();
            ffmpegOutputService.Stop();

            ffmpegSlides.Clear();
            ffmpegCurrentSlideIndex = 0;

            ffmpegSlides = GetActiveSlidesForFfmpeg();

            if (ffmpegSlides.Count == 0)
            {
                MessageBox.Show("Нет активного периода или в нём нет медиа.");
                return;
            }

            StartCurrentFfmpegSlide();
        }


        private List<SlideItem> GetActiveSlidesForFfmpeg()
        {
            DateTime now = DateTime.Now;

            var activePeriod = SettingsData.Periods?
                .Where(p => now >= p.StartDateTime && now < p.EndDateTime)
                .OrderBy(p => p.StartDateTime)
                .FirstOrDefault();

            if (activePeriod == null)
                return new List<SlideItem>();

            return activePeriod.Slides.ToList();
        }
        private TimeSpan GetSlideDurationForFfmpeg(SlideItem slide)
        {
            if (slide.Type == MediaType.Image)
            {
                int seconds = slide.DurationSeconds > 0 ? slide.DurationSeconds : 5;
                return TimeSpan.FromSeconds(seconds);
            }

            if (!slide.PlayFullVideo && slide.EndSeconds > slide.StartSeconds)
            {
                return TimeSpan.FromSeconds(slide.EndSeconds - slide.StartSeconds);
            }

            if (slide.DurationSeconds > 0)
                return TimeSpan.FromSeconds(slide.DurationSeconds);

            double realDuration = ffmpegOutputService.GetMediaDurationSeconds(slide.Path, "ffmpeg.exe");

            if (realDuration > 0)
                return TimeSpan.FromSeconds(realDuration);

            return TimeSpan.FromSeconds(30);
        }


        private void FfmpegPlaybackTimer_Tick(object? sender, EventArgs e)
        {
            ffmpegPlaybackTimer.Stop();

            if (ffmpegSlides.Count == 0)
                return;

            ffmpegOutputService.Stop();

            ffmpegCurrentSlideIndex++;

            if (ffmpegCurrentSlideIndex >= ffmpegSlides.Count)
                ffmpegCurrentSlideIndex = 0;

            StartCurrentFfmpegSlide();
        }


        private void StartCurrentFfmpegSlide()
        {
            if (ffmpegSlides.Count == 0)
                return;

            if (ffmpegCurrentSlideIndex < 0 || ffmpegCurrentSlideIndex >= ffmpegSlides.Count)
                ffmpegCurrentSlideIndex = 0;

            SlideItem slide = ffmpegSlides[ffmpegCurrentSlideIndex];

            string codec = "libx264";
            if (CodecComboBox.SelectedItem is ComboBoxItem codecItem)
            {
                string selectedCodec = codecItem.Content?.ToString() ?? "H264";

                if (selectedCodec == "HEVC")
                    codec = "libx265";
                else if (selectedCodec == "MPEG2")
                    codec = "mpeg2video";
                else
                    codec = "libx264";
            }

            string preset = "veryfast";
            if (PresetComboBox.SelectedItem is ComboBoxItem presetItem)
            {
                preset = presetItem.Content?.ToString() ?? "veryfast";
            }

            int bitrate = 4000;
            int fps = 25;
            int width = 1920;
            int height = 1080;

            int.TryParse(BitrateTextBox.Text, out bitrate);
            int.TryParse(FpsTextBox.Text, out fps);
            int.TryParse(WidthTextBox.Text, out width);
            int.TryParse(HeightTextBox.Text, out height);

            if (bitrate <= 0) bitrate = 4000;
            if (fps <= 0) fps = 25;
            if (width <= 0) width = 1920;
            if (height <= 0) height = 1080;

            string outputUrl = OutputUrlTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputUrl))
            {
                MessageBox.Show("Укажи URL потока.");
                return;
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

            try
            {
                ffmpegOutputService.StartSlide(slide, settings);

                ffmpegPlaybackTimer.Stop();
                ffmpegPlaybackTimer.Interval = GetSlideDurationForFfmpeg(slide);
                ffmpegPlaybackTimer.Start();

                
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска элемента FFmpeg:\n{ex.Message}");
            }
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