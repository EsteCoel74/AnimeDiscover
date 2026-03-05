using System.Windows;
using System.Windows.Controls;
using AnimeDiscover.Services;

namespace AnimeDiscover.Views
{
    /// <summary>
    /// Interaction logic for AnimePage.xaml
    /// </summary>
    public partial class AnimePage : UserControl
    {
        public AnimePage()
        {
            InitializeComponent();
            this.Loaded += AnimePage_Loaded;
        }

        private void AnimePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnimeController controller && controller.CurrentAnime != null)
            {
                // Load genres
                var genresControl = FindName("GenresItemsControl") as ItemsControl;
                if (genresControl != null && controller.CurrentAnime.Genres != null)
                {
                    genresControl.ItemsSource = controller.CurrentAnime.Genres;
                }

                // Load trailer if available
                var trailerBrowser = FindName("TrailerBrowser") as WebBrowser;
                if (trailerBrowser != null && !string.IsNullOrEmpty(controller.CurrentAnime.TrailerUrl))
                {
                    trailerBrowser.Navigate(controller.CurrentAnime.TrailerUrl);
                }
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnimeController controller)
            {
                controller.GoBack();
            }
        }

        private void WatchedCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnimeController controller && sender is CheckBox checkBox)
            {
                controller.UpdateIsWatched(checkBox.IsChecked ?? false);
            }
        }

        private void ScoreSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataContext is AnimeController controller && sender is Slider slider)
            {
                controller.UpdateUserScore((int)slider.Value);
            }
        }
    }
}
