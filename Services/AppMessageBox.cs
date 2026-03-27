using System.Windows;
using AnimeDiscover.Views;

// Façade statique pour afficher une boîte de dialogue harmonisée.
namespace AnimeDiscover.Services
{
    public static class AppMessageBox
    {
        // Affiche une boîte de message personnalisée et retourne le choix utilisateur.
        public static MessageBoxResult Show(string message, string title = "AnimeDiscover", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
        {
            var dialog = new AppMessageBoxWindow(message, title, buttons, image)
            {
                Owner = Application.Current?.MainWindow
            };

            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}