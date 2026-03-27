using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AnimeDiscover.Services;

// Gère la personnalisation utilisateur dans la page paramètres.
namespace AnimeDiscover.Views
{
    public partial class SettingsPage : UserControl
    {
        private readonly UserDataService _userDataService;
        private readonly Action _onSaved;
        private readonly Action _onCancelled;
        private ComboBox? LanguageSelector => FindName("LanguageComboBox") as ComboBox;
        private ToggleButton? LightThemeToggle => FindName("LightThemeToggleButton") as ToggleButton;
        private ToggleButton? DarkThemeToggle => FindName("DarkThemeToggleButton") as ToggleButton;
        private ToggleButton? AnimationsOnToggle => FindName("AnimationsOnToggleButton") as ToggleButton;
        private ToggleButton? AnimationsOffToggle => FindName("AnimationsOffToggleButton") as ToggleButton;

        // Initialise la page paramètres avec les callbacks de navigation.
        public SettingsPage(UserDataService userDataService, Action onSaved, Action onCancelled)
        {
            _userDataService = userDataService;
            _onSaved = onSaved;
            _onCancelled = onCancelled;

            InitializeComponent();
            WireUpUiHandlers();
            InitializeLanguageSelector();
            InitializeThemeSelector();
            InitializeAnimationsSelector();
        }

        // Branche les événements UI nécessaires après le chargement XAML.
        private void WireUpUiHandlers()
        {
            if (AnimationsOnToggle != null)
            {
                AnimationsOnToggle.Click += AnimationsOnToggleButton_Click;
            }

            if (AnimationsOffToggle != null)
            {
                AnimationsOffToggle.Click += AnimationsOffToggleButton_Click;
            }
        }

        // Initialise la langue UI selon la préférence enregistrée.
        private void InitializeLanguageSelector()
        {
            if (LanguageSelector == null)
            {
                return;
            }

            LanguageSelector.ItemsSource = UiPreferencesManager.GetAvailableLanguages();
            LanguageSelector.SelectedValuePath = nameof(UiPreferencesManager.UiLanguageOption.Code);
            LanguageSelector.SelectedValue = UiPreferencesManager.NormalizeLanguage(_userDataService.GetUiLanguage());
        }

        // Initialise les boutons toggle de thème selon la préférence enregistrée.
        private void InitializeThemeSelector()
        {
            if (LightThemeToggle == null || DarkThemeToggle == null)
            {
                return;
            }

            ApplyThemeSelectionToToggles(ThemeManager.NormalizeTheme(_userDataService.GetTheme()));
        }

        // Active le thème clair quand le toggle correspondant est sélectionné.
        private void LightThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyThemeSelectionToToggles(ThemeManager.LightTheme);
        }

        // Active le thème sombre quand le toggle correspondant est sélectionné.
        private void DarkThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyThemeSelectionToToggles(ThemeManager.DarkTheme);
        }

        // Active les animations UI.
        private void AnimationsOnToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyAnimationSelectionToToggles(true);
        }

        // Désactive les animations UI.
        private void AnimationsOffToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyAnimationSelectionToToggles(false);
        }

        // Met à jour l'état visuel des toggles pour ne garder qu'un thème actif.
        private void ApplyThemeSelectionToToggles(string themeName)
        {
            if (LightThemeToggle == null || DarkThemeToggle == null)
            {
                return;
            }

            var normalizedTheme = ThemeManager.NormalizeTheme(themeName);
            var isDark = string.Equals(normalizedTheme, ThemeManager.DarkTheme, StringComparison.OrdinalIgnoreCase);

            LightThemeToggle.IsChecked = !isDark;
            DarkThemeToggle.IsChecked = isDark;
        }

        // Initialise les toggles d'animation selon la préférence enregistrée.
        private void InitializeAnimationsSelector()
        {
            ApplyAnimationSelectionToToggles(_userDataService.GetUiAnimationsEnabled());
        }

        // Met à jour l'état visuel des toggles d'animations.
        private void ApplyAnimationSelectionToToggles(bool isEnabled)
        {
            if (AnimationsOnToggle == null || AnimationsOffToggle == null)
            {
                return;
            }

            AnimationsOnToggle.IsChecked = isEnabled;
            AnimationsOffToggle.IsChecked = !isEnabled;
        }

        // Retourne le thème actuellement choisi dans les boutons toggle.
        private string GetSelectedTheme()
        {
            var isDarkSelected = DarkThemeToggle?.IsChecked == true;
            return isDarkSelected ? ThemeManager.DarkTheme : ThemeManager.LightTheme;
        }

        // Retourne la langue UI actuellement choisie.
        private string GetSelectedLanguage()
        {
            return UiPreferencesManager.NormalizeLanguage(LanguageSelector?.SelectedValue?.ToString());
        }

        // Retourne l'état des animations UI choisi dans les toggles.
        private bool GetSelectedAnimationsEnabled()
        {
            return AnimationsOnToggle?.IsChecked != false;
        }

        // Enregistre le thème puis revient à l'écran précédent.
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedLanguage = GetSelectedLanguage();
            var selectedTheme = GetSelectedTheme();
            var selectedAnimationsEnabled = GetSelectedAnimationsEnabled();

            _userDataService.SaveUiLanguage(selectedLanguage);
            _userDataService.SaveTheme(selectedTheme);
            _userDataService.SaveUiAnimationsEnabled(selectedAnimationsEnabled);

            UiPreferencesManager.ApplyLanguage(selectedLanguage);
            ThemeManager.ApplyTheme(selectedTheme);
            UiPreferencesManager.ApplyAnimations(selectedAnimationsEnabled);

            AppMessageBox.Show(
                UiPreferencesManager.GetText("Ui.LanguageSaved", "Paramètres UI enregistrés."),
                UiPreferencesManager.GetText("Ui.SettingsTitle", "Paramètres"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _onSaved?.Invoke();
        }

        // Annule les changements et revient à l'écran précédent.
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _onCancelled?.Invoke();
        }
    }
}
