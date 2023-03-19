using System;
using IOPath = System.IO.Path;

namespace Xfixy.WinUI
{
    internal static class Funcs
    {
        public static string GetScriptsLocation()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string fullPath = IOPath.Join(localAppData, "Xfixy", "Ps1-scripts");
            return fullPath;
        }
    }
}
