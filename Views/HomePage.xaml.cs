using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AnimeDiscover.Models;
using AnimeDiscover.Services;

// Gère les interactions de la page d'accueil et des cartes anime.
namespace AnimeDiscover.Views
{
    /// <summary>
    /// Interaction logic for HomePage.xaml
    /// </summary>
    public partial class HomePage : UserControl
    {
        public HomePage()
        {
            InitializeComponent();
        }

        // Ouvre la fiche anime lors d'un clic sur la carte.
        private void AnimeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Get the anime from the clicked card
            if (sender is Border border)
            {
                var anime = border.Tag;

                // Get the controller from the DataContext and call SelectAnime
                var controller = DataContext;
                var selectAnimeMethod = controller?.GetType().GetMethod("SelectAnime");
                if (selectAnimeMethod != null && anime != null)
                {
                    selectAnimeMethod.Invoke(controller, new[] { anime });
                }
            }
        }

        // Ouvre la page de détail via le bouton de détail (si présent).
        private void DetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Datum anime && DataContext is HomeController controller)
            {
                controller.SelectAnime(anime);
            }
        }

        // Ajoute l'anime sélectionné à la liste utilisateur.
        private void AddToMyListButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Datum anime && DataContext is HomeController controller)
            {
                controller.AddToMyList(anime);
            }
        }
    }
}
