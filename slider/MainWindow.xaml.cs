using System.Windows;

namespace slider
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OpenSlider_Click(object sender, RoutedEventArgs e)
        {
            SliderWindow slider = new SliderWindow();
            slider.Show();
        }
    }
}