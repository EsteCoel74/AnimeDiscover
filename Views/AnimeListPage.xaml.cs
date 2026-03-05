using System.Windows;
using System.Windows.Controls;
using AnimeDiscover.Services;

namespace AnimeDiscover.Views
{
    /// <summary>
    /// Interaction logic for AnimeListPage.xaml
    /// </summary>
    public partial class AnimeListPage : UserControl
    {
        public AnimeListPage()
        {
            InitializeComponent();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnimeListController controller)
            {
                controller.GoBack();
            }
        }
    }
}
