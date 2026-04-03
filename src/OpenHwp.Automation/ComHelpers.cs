using System.Runtime.InteropServices;

namespace OpenHwp.Automation
{
    internal static class ComHelpers
    {
        public static void SafeRelease(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject))
            {
                return;
            }

            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
