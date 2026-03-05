using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
    }
}
