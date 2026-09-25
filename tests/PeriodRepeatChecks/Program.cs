using slider;
using slider.Models;
using slider.Services;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class Program
{
    private static int checks;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private)!.Invoke(target, args);
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private | BindingFlags.Public)!.GetValue(target)!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    [STAThread]
    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        string folder = Path.Combine(AppContext.BaseDirectory, "checks");
        Directory.CreateDirectory(folder);
        Check(new PlaylistPeriod().RepeatCount == 1, "New period default");
        foreach (var (input, expected) in new[] { (-1, 1), (0, 1), (1, 1), (2, 2), (99, 99), (100, 99) })
            Check(new PlaylistPeriod { RepeatCount = input }.RepeatCount == expected, "Model bounds");

        string jsonPath = Path.Combine(folder, "playlist.json");
        File.WriteAllText(jsonPath, "{\"Periods\":[{\"Name\":\"Legacy\",\"Slides\":[]}]}");
        Check(PlaylistFileService.Load(jsonPath).Periods[0].RepeatCount == 1, "Legacy JSON");
        foreach (int repeat in new[] { 1, 2, 99 })
        {
            PlaylistFileService.Save(jsonPath, new PlaylistData
            {
                Periods = new() { new PlaylistPeriod { Name = "Saved", RepeatCount = repeat } }
            });
            Check(PlaylistFileService.Load(jsonPath).Periods[0].RepeatCount == repeat, "JSON roundtrip");
        }

        var editor = new MainWindow();
        var loadedSettings = new PlaylistData
        {
            PlaylistName = "Saved playlist name",
            StreamSettings = new StreamSettings { OutputUrl = "udp://127.0.0.1:1234", BitrateKbps = 6789 },
            Periods = new() { new PlaylistPeriod() }
        };
        Call(editor, "ApplyPlaylistData", loadedSettings);
        var savedSettings = (PlaylistData)Call(editor, "BuildPlaylistData")!;
        Check(savedSettings.PlaylistName == loadedSettings.PlaylistName, "Playlist name survives load/save");
        Check(savedSettings.StreamSettings.OutputUrl == loadedSettings.StreamSettings.OutputUrl &&
            savedSettings.StreamSettings.BitrateKbps == 6789, "Stream settings survive load/save");
        var settingsWindow = new SettingsWindow(savedSettings);
        Check(((TextBox)settingsWindow.FindName("BitrateTextBox")).Text == "6789", "Settings window loads saved values");
        settingsWindow.Close();
        var inputBox = (TextBox)editor.FindName("RepeatCountTextBox");
        var period = Field<PlaylistPeriod>(editor, "selectedPeriod");
        string name = period.Name;
        DateTime start = period.StartDateTime;
        Check(inputBox.Text == "1", "Editor default");
        foreach (string text in new[] { "1", "2", "99" })
        {
            inputBox.SelectAll();
            var typing = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                new TextComposition(InputManager.Current, inputBox, text)) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            Call(editor, "RepeatCount_PreviewTextInput", inputBox, typing);
            Check(!typing.Handled, "Valid typed value");
            inputBox.SelectedText = text;
            Check(period.RepeatCount == int.Parse(text), "Immediate model update");
        }
        foreach (string invalid in new[] { "0", "100", "-1", "1.5", "abc" })
        {
            inputBox.SelectAll();
            var typing = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                new TextComposition(InputManager.Current, inputBox, invalid)) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            Call(editor, "RepeatCount_PreviewTextInput", inputBox, typing);
            Check(typing.Handled, "Invalid typing rejected");
            var paste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, invalid),
                false, DataFormats.UnicodeText);
            Call(editor, "RepeatCount_Pasting", inputBox, paste);
            Check(paste.CommandCancelled, "Invalid paste rejected");
        }
        var validPaste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, "23"),
            false, DataFormats.UnicodeText);
        Call(editor, "RepeatCount_Pasting", inputBox, validPaste);
        Check(!validPaste.CommandCancelled, "Valid paste accepted");
        inputBox.SelectedText = "23";
        Check(period.RepeatCount == 23, "Pasted model value");
        inputBox.Text = "";
        Call(editor, "RepeatCount_LostFocus", inputBox, new RoutedEventArgs());
        Check(inputBox.Text == "1" && period.RepeatCount == 1, "Empty normalization");
        inputBox.Text = "invalid";
        Check(inputBox.Text == "1" && period.RepeatCount == 1, "Invalid normalization");
        foreach (var (value, delta, expected) in new[] { (1, 120, 2), (2, -120, 1), (1, -120, 1), (99, 120, 99) })
        {
            inputBox.Text = value.ToString();
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            Call(editor, "RepeatCount_MouseWheel", inputBox, wheel);
            Check(wheel.Handled && period.RepeatCount == expected && inputBox.Text == expected.ToString(), "Wheel and bounds");
        }
        Check(period.Name == name && period.StartDateTime == start, "Other properties preserved");
        ((TextBox)editor.FindName("PlaylistPathTextBox")).Text = jsonPath;
        ((CheckBox)editor.FindName("AutoSaveCheckBox")).IsChecked = true;
        inputBox.Text = "2";
        Check(Field<DispatcherTimer>(editor, "autoSaveDelayTimer").IsEnabled, "Typed value schedules autosave");
        Call(editor, "AutoSaveDelayTimer_Tick", editor, EventArgs.Empty);
        Check(PlaylistFileService.Load(jsonPath).Periods[0].RepeatCount == 2, "Typed value autosaved");
        Call(editor, "RepeatCount_MouseWheel", inputBox,
            new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        Call(editor, "AutoSaveDelayTimer_Tick", editor, EventArgs.Empty);
        Check(PlaylistFileService.Load(jsonPath).Periods[0].RepeatCount == 3, "Wheel value autosaved");
        ((CheckBox)editor.FindName("AutoSaveCheckBox")).IsChecked = false;
        inputBox.Text = "99";
        PlaylistFileService.Save(jsonPath, (PlaylistData)Call(editor, "BuildPlaylistData")!);
        Check(PlaylistFileService.Load(jsonPath).Periods[0].RepeatCount == 99, "Editor value persisted");

        Call(editor, "AddDayGroupButton_Click", editor, new RoutedEventArgs());
        Check(inputBox.Text == "1", "Add period resets editor");
        var periods = Field<List<PlaylistPeriod>>(editor, "periods");
        var list = (ListBox)editor.FindName("DaysListBox");
        list.SelectedItem = period;
        Check(inputBox.Text == "99", "Selection restores repeat count");
        Call(editor, "DeletePeriod_Click", new Button { DataContext = period }, new RoutedEventArgs());
        Check(inputBox.Text == "1" && periods.Count == 1, "Deleting selected period");
        var hour = (TextBox)editor.FindName("StartHourTextBox");
        Check(ReferenceEquals(inputBox.Style, hour.Style), "Existing numeric style reused");

        foreach (var size in new[] { new Size(1400, 850), new Size(1200, 760) })
        {
            var content = (FrameworkElement)editor.Content;
            editor.Content = null;
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(folder, $"editor-{size.Width}.png"));
            png.Save(output);
            var nameBox = (TextBox)editor.FindName("PeriodNameTextBox");
            Check(inputBox.ActualHeight == nameBox.ActualHeight && inputBox.ActualWidth == 48,
                "Numeric field dimensions");
            var apply = (Button)editor.FindName("ApplyPeriodSettingsButton");
            var weekdays = (FrameworkElement)editor.FindName("SundayToggleButton");
            Check(weekdays.TranslatePoint(new Point(0, weekdays.ActualHeight), content).Y <=
                apply.TranslatePoint(new Point(), content).Y, "Properties do not overlap apply button");
            editor.Content = content;
        }
        editor.Close();

        string imagePath = Path.Combine(folder, "slide.png");
        var frame = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using (var output = File.Create(imagePath)) encoder.Save(output);
        SlideItem Slide() => new() { Path = imagePath, Type = MediaType.Image, TransitionEffect = "Нет", DurationSeconds = 1 };
        foreach (int repeat in new[] { 1, 2, 3, 99 })
        {
            var first = new PlaylistPeriod { RepeatCount = repeat, Slides = new() { Slide(), Slide() } };
            var second = new PlaylistPeriod { RepeatCount = 2, Slides = new() { Slide() } };
            var playbackPeriods = new List<PlaylistPeriod> { new() { RepeatCount = 99 }, first, second };
            var player = new SliderWindow(playbackPeriods, "");
            for (int cycle = 0; cycle < 2; cycle++)
            {
                for (int pass = 1; pass <= repeat; pass++)
                {
                    Check(Field<int>(player, "currentIndex") == 0 && Field<int>(player, "currentPlayback") == pass, "Period start/pass");
                    Call(player, "ShowNextSlide");
                    Check(Field<int>(player, "currentIndex") == 1 && Field<int>(player, "currentPlayback") == pass, "Entire period repeats");
                    Call(player, "ShowNextSlide");
                }
                Check(Field<int>(player, "currentIndex") == 2 && Field<int>(player, "currentPlayback") == 1, "Next period reset");
                Call(player, "ShowNextSlide");
                Check(Field<int>(player, "currentIndex") == 2 && Field<int>(player, "currentPlayback") == 2, "Single slide repeats");
                Call(player, "ShowNextSlide");
            }
            Call(player, "ReloadActiveSlides", false);
            Check(Field<int>(player, "currentPlayback") == 1, "Unchanged schedule");
            first.RepeatCount = 3;
            Call(player, "ReloadActiveSlides", false);
            Call(player, "ShowNextSlide");
            Call(player, "ShowNextSlide");
            Check(Field<int>(player, "currentPlayback") == 2, "Edited repeat applies");
            Call(player, "ReloadActiveSlides", false);
            Check(Field<int>(player, "currentPlayback") == 2, "Schedule poll preserves pass");
            Call(player, "ReloadActiveSlides", true);
            Check(Field<int>(player, "currentIndex") == 0 && Field<int>(player, "currentPlayback") == 1, "Forced restart resets");
            Call(player, "ShowNextSlide");
            Call(player, "ShowNextSlide");
            first.EndDateTime = DateTime.Now.AddSeconds(-1);
            Call(player, "ReloadActiveSlides", false);
            Check(Field<List<SlideItem>>(player, "currentSlides").Count == 1 && Field<int>(player, "currentPlayback") == 1,
                "Expired period removed and counter reset");
            first.EndDateTime = DateTime.Today.AddDays(1);
            player.Close();
            Check(Field<int>(player, "currentPlayback") == 1 && !Field<DispatcherTimer>(player, "slideTimer").IsEnabled &&
                !Field<DispatcherTimer>(player, "periodCheckTimer").IsEnabled, "Close resets/stops playback");
            var restarted = new SliderWindow(playbackPeriods, "");
            Check(Field<int>(restarted, "currentIndex") == 0 && Field<int>(restarted, "currentPlayback") == 1, "New run resets");
            playbackPeriods.Clear();
            Call(restarted, "ReloadActiveSlides", false);
            Check(Field<List<SlideItem>>(restarted, "currentSlides").Count == 0 &&
                !Field<DispatcherTimer>(restarted, "slideTimer").IsEnabled, "Empty schedule stops");
            restarted.Close();
        }
        var broken = new SliderWindow(new() { new PlaylistPeriod { RepeatCount = 99,
            Slides = new() { new SlideItem { Path = Path.Combine(folder, "missing.png") } } } }, "");
        Check(Field<DispatcherTimer>(broken, "slideTimer").IsEnabled, "Missing media defers retry without recursion");
        broken.Close();
        Console.WriteLine($"PASS: {checks} checks. Layout renders: {folder}");
        app.Shutdown();
    }
}
