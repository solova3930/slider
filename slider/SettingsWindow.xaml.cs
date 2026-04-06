using slider.Models;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace slider
{
    public partial class SettingsWindow : Window
    {
        public PlaylistData SettingsData { get; private set; }

        public SettingsWindow(PlaylistData data)
        {
            InitializeComponent();
            Loaded += SliderWindow_Loaded;
            SettingsData = data;

            LoadDataToForm();
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