using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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
        private string _trailerUrl;

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

                // Trailer link (external browser for reliability)
                var trailerUrl = controller.CurrentAnime.TrailerUrl;
                if (!string.IsNullOrWhiteSpace(trailerUrl) && Uri.IsWellFormedUriString(trailerUrl, UriKind.Absolute))
                {
                    _trailerUrl = trailerUrl;
                    TrailerSection.Visibility = Visibility.Visible;
                }
                else
                {
                    _trailerUrl = null;
                    TrailerSection.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void OpenTrailerButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_trailerUrl))
            {
                MessageBox.Show("Bande annonce indisponible pour cet anime.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _trailerUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossible d'ouvrir la bande annonce: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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
