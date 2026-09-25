using slider.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace slider
{
    public partial class MainWindow
    {
        private static bool IsValidRepeatCount(string text)
        {
            return text.Length > 0 && text.All(c => c >= '0' && c <= '9') &&
                int.TryParse(text, out int value) && value >= 1 && value <= 99;
        }

        private static bool CanInsertRepeatCount(TextBox textBox, string text)
        {
            string result = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
                .Insert(textBox.SelectionStart, text);
            return IsValidRepeatCount(result);
        }

        private void RepeatCount_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox textBox)
                e.Handled = !CanInsertRepeatCount(textBox, e.Text);
        }

        private void RepeatCount_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is TextBox textBox &&
                (e.DataObject.GetData(DataFormats.UnicodeText) is not string text ||
                 !CanInsertRepeatCount(textBox, text)))
                e.CancelCommand();
        }

        private void RepeatCount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.Tag is not PlaylistPeriod period)
                return;

            int value = IsValidRepeatCount(textBox.Text) ? int.Parse(textBox.Text) : 1;
            if (textBox.Text.Length > 0 && !IsValidRepeatCount(textBox.Text))
                textBox.SetCurrentValue(TextBox.TextProperty, "1");

            if (period.RepeatCount == value)
                return;

            period.RepeatCount = value;
            ScheduleAutoSave();
        }

        private void RepeatCount_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                int value = IsValidRepeatCount(textBox.Text) ? int.Parse(textBox.Text) : 1;
                textBox.SetCurrentValue(TextBox.TextProperty, value.ToString());
            }
        }

        private void RepeatCount_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not TextBox textBox || !textBox.IsEnabled || e.Delta == 0)
                return;

            int value = IsValidRepeatCount(textBox.Text) ? int.Parse(textBox.Text) : 1;
            value = Math.Clamp(value + (e.Delta > 0 ? 1 : -1), 1, 99);
            textBox.SetCurrentValue(TextBox.TextProperty, value.ToString());
            e.Handled = true;
        }
    }
}
