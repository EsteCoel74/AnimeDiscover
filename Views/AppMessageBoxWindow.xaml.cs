using System.Windows;
using System.Windows.Media;

// Fenêtre personnalisée utilisée pour afficher les messages applicatifs.
namespace AnimeDiscover.Views
{
    public partial class AppMessageBoxWindow : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        // Initialise la boîte de message personnalisée avec son contenu et ses boutons.
        public AppMessageBoxWindow(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            InitializeComponent();

            TitleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "AnimeDiscover" : title;
            MessageTextBlock.Text = message;

            ConfigureImage(image);
            ConfigureButtons(buttons);
        }

        // Configure l'icône affichée selon le type de message.
        private void ConfigureImage(MessageBoxImage image)
        {
            switch (image)
            {
                case MessageBoxImage.Error:
                    IconTextBlock.Text = "⛔";
                    IconTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                    break;
                case MessageBoxImage.Question:
                    IconTextBlock.Text = "❓";
                    IconTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                    break;
                case MessageBoxImage.Warning:
                    IconTextBlock.Text = "⚠";
                    IconTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    break;
                case MessageBoxImage.Information:
                default:
                    IconTextBlock.Text = "ℹ";
                    IconTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4F46E5"));
                    break;
            }
        }

        // Affiche les boutons requis selon le mode demandé.
        private void ConfigureButtons(MessageBoxButton buttons)
        {
            OkButton.Visibility = Visibility.Collapsed;
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = Visibility.Collapsed;

            switch (buttons)
            {
                case MessageBoxButton.YesNo:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.OK:
                default:
                    OkButton.Visibility = Visibility.Visible;
                    break;
            }
        }

        // Valide la boîte de dialogue avec un résultat OK.
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            DialogResult = true;
            Close();
        }

        // Valide la boîte de dialogue avec un résultat Yes.
        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            DialogResult = true;
            Close();
        }

        // Ferme la boîte de dialogue avec un résultat No.
        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            DialogResult = false;
            Close();
        }
    }
}