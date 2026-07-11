using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace GalgameUiTranslator
{
    public static class FontManager
    {
        private static readonly PrivateFontCollection PrivateFonts = new PrivateFontCollection();
        private static readonly HashSet<string> LoadedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();

        public static IReadOnlyList<string> LoadFontFile(string path)
        {
            var fullPath = Path.GetFullPath(path);
            lock (Sync)
            {
                if (!LoadedFiles.Contains(fullPath))
                {
                    PrivateFonts.AddFontFile(fullPath);
                    LoadedFiles.Add(fullPath);
                }

                return PrivateFonts.Families
                    .Select(family => family.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
        }

        public static FontFamily ResolveFamily(string name)
        {
            lock (Sync)
            {
                var privateMatch = PrivateFonts.Families.FirstOrDefault(
                    family => string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase));
                if (privateMatch != null)
                {
                    return new FontFamily(privateMatch.Name, PrivateFonts);
                }
            }

            try
            {
                return new FontFamily(name);
            }
            catch
            {
                return new FontFamily(FontFamily.GenericSansSerif.Name);
            }
        }

        public static bool HasFontFamily(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            lock (Sync)
            {
                if (PrivateFonts.Families.Any(
                        family => string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            try
            {
                using (var family = new FontFamily(name))
                {
                    return !string.IsNullOrWhiteSpace(family.Name);
                }
            }
            catch
            {
                return false;
            }
        }

        public static IReadOnlyList<string> GetAvailableFontNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var installed = new InstalledFontCollection())
            {
                foreach (var family in installed.Families)
                {
                    names.Add(family.Name);
                }
            }

            lock (Sync)
            {
                foreach (var family in PrivateFonts.Families)
                {
                    names.Add(family.Name);
                }
            }

            return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
    }
}
