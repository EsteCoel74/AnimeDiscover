using System.Windows;
using System.Windows.Media;

// Gère l'application des thèmes visuels globaux de l'application.
namespace AnimeDiscover.Services
{
    public static class ThemeManager
    {
        public const string LightTheme = "Clair";
        public const string DarkTheme = "Sombre";

        // Retourne les thèmes supportés pour l'interface des paramètres.
        public static string[] GetAvailableThemes()
        {
            return new[] { LightTheme, DarkTheme };
        }

        // Valide et normalise le nom de thème utilisé dans l'application.
        public static string NormalizeTheme(string? themeName)
        {
            if (string.Equals(themeName, DarkTheme, System.StringComparison.OrdinalIgnoreCase))
            {
                return DarkTheme;
            }

            return LightTheme;
        }

        // Applique le thème demandé en mettant à jour les ressources dynamiques globales.
        public static void ApplyTheme(string? themeName)
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            var selectedTheme = NormalizeTheme(themeName);
            if (selectedTheme == DarkTheme)
            {
                ApplyDarkTheme(app.Resources);
                return;
            }

            ApplyLightTheme(app.Resources);
        }

        // Applique les ressources de couleur du thème clair.
        private static void ApplyLightTheme(ResourceDictionary resources)
        {
            SetBrush(resources, "WindowBackgroundBrush", "#F5F7FB");
            SetBrush(resources, "SurfaceBrush", "#FFFFFF");
            SetBrush(resources, "SurfaceAltBrush", "#EEF2FF");
            SetBrush(resources, "HeaderBackgroundBrush", "#1F2343");
            SetBrush(resources, "BorderBrushSoft", "#D9E0EC");
            SetBrush(resources, "TextPrimaryColor", "#1F2937");
            SetBrush(resources, "TextSecondaryColor", "#6B7280");
            SetBrush(resources, "AccentBrush", "#4F46E5");
            SetBrush(resources, "AccentHoverBrush", "#4338CA");
            SetBrush(resources, "SuccessBrush", "#16A34A");
            SetBrush(resources, "SuccessHoverBrush", "#15803D");
            SetBrush(resources, "InfoBrush", "#2563EB");
            SetBrush(resources, "InfoHoverBrush", "#1D4ED8");
            SetBrush(resources, "HeaderSubtleButtonBrush", "#2D3466");
            SetBrush(resources, "HeaderSubtleButtonHoverBrush", "#3A4380");
            SetBrush(resources, "DangerBrush", "#DC2626");
            SetBrush(resources, "DangerHoverBrush", "#B91C1C");
            SetBrush(resources, "ScrollbarThumbBrush", "#C1CADB");
            SetBrush(resources, "ScrollbarThumbHoverBrush", "#AAB6CC");

            resources["MainGradientBrush"] = CreateGradient("#1A164B", "#7B75D8");
        }

        // Applique les ressources de couleur du thème sombre.
        private static void ApplyDarkTheme(ResourceDictionary resources)
        {
            SetBrush(resources, "WindowBackgroundBrush", "#0F1320");
            SetBrush(resources, "SurfaceBrush", "#1B2233");
            SetBrush(resources, "SurfaceAltBrush", "#253049");
            SetBrush(resources, "HeaderBackgroundBrush", "#0C1020");
            SetBrush(resources, "BorderBrushSoft", "#36435C");
            SetBrush(resources, "TextPrimaryColor", "#E5E7EB");
            SetBrush(resources, "TextSecondaryColor", "#B4BCD0");
            SetBrush(resources, "AccentBrush", "#818CF8");
            SetBrush(resources, "AccentHoverBrush", "#6366F1");
            SetBrush(resources, "SuccessBrush", "#22C55E");
            SetBrush(resources, "SuccessHoverBrush", "#16A34A");
            SetBrush(resources, "InfoBrush", "#60A5FA");
            SetBrush(resources, "InfoHoverBrush", "#3B82F6");
            SetBrush(resources, "HeaderSubtleButtonBrush", "#1F2A44");
            SetBrush(resources, "HeaderSubtleButtonHoverBrush", "#2A3B62");
            SetBrush(resources, "DangerBrush", "#F87171");
            SetBrush(resources, "DangerHoverBrush", "#EF4444");
            SetBrush(resources, "ScrollbarThumbBrush", "#4B5568");
            SetBrush(resources, "ScrollbarThumbHoverBrush", "#667085");

            resources["MainGradientBrush"] = CreateGradient("#0E152B", "#2B3F6D");
        }

        // Met à jour ou crée un SolidColorBrush dans les ressources.
        private static void SetBrush(ResourceDictionary resources, string key, string colorHex)
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            resources[key] = new SolidColorBrush(color);
        }

        // Crée le dégradé principal utilisé en arrière-plan des pages.
        private static LinearGradientBrush CreateGradient(string topColorHex, string bottomColorHex)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };

            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(topColorHex), 0.0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(bottomColorHex), 1.0));
            return brush;
        }
    }
}
