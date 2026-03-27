using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;

// Gère l'application globale des préférences UI (langue et animations).
namespace AnimeDiscover.Services
{
    public static class UiPreferencesManager
    {
        public const string DefaultLanguage = "fr-FR";

        // Représente une langue disponible dans les paramètres.
        public sealed class UiLanguageOption
        {
            public required string Code { get; init; }
            public required string DisplayName { get; init; }
        }

        // Retourne la liste des langues disponibles dans l'application.
        public static IReadOnlyList<UiLanguageOption> GetAvailableLanguages()
        {
            return new List<UiLanguageOption>
            {
                new() { Code = "fr-FR", DisplayName = "Français" },
                new() { Code = "en-US", DisplayName = "English" }
            };
        }

        // Normalise un code langue pour éviter les valeurs invalides.
        public static string NormalizeLanguage(string? languageCode)
        {
            var normalized = (languageCode ?? string.Empty).Trim();
            var isSupported = GetAvailableLanguages().Any(x => string.Equals(x.Code, normalized, StringComparison.OrdinalIgnoreCase));
            return isSupported ? normalized : DefaultLanguage;
        }

        // Applique la langue UI au thread courant et aux prochains threads.
        public static void ApplyLanguage(string? languageCode)
        {
            var normalized = NormalizeLanguage(languageCode);
            var culture = new CultureInfo(normalized);

            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            var merged = app.Resources.MergedDictionaries;
            var existingStringsDictionary = merged.FirstOrDefault(d =>
                d.Source != null
                && d.Source.OriginalString.Contains("Themes/Strings.", StringComparison.OrdinalIgnoreCase));

            var localizedDictionary = new ResourceDictionary
            {
                Source = new Uri($"Themes/Strings.{normalized}.xaml", UriKind.Relative)
            };

            if (existingStringsDictionary != null)
            {
                var index = merged.IndexOf(existingStringsDictionary);
                merged[index] = localizedDictionary;
            }
            else
            {
                merged.Add(localizedDictionary);
            }
        }

        // Applique la préférence des animations UI au niveau de l'application.
        public static void ApplyAnimations(bool isEnabled)
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            app.Resources["UiAnimationsEnabled"] = isEnabled;
        }

        // Retourne une chaîne localisée depuis les ressources avec fallback sécurisé.
        public static string GetText(string key, string fallback)
        {
            var resourceValue = Application.Current?.TryFindResource(key) as string;
            return string.IsNullOrWhiteSpace(resourceValue) ? fallback : resourceValue;
        }
    }
}
