using System.Diagnostics;

namespace StudyFocus
{
    internal static class ProductBrand
    {
        internal const string Name = "定心";
        internal const string LegacyName = "专注守门";
        internal const string Company = "Study Focus";
        internal const string EmergencyName = Name + " · 应急停止";

        internal static bool Matches(FileVersionInfo details)
        {
            return details.CompanyName == Company &&
                (details.ProductName == Name || details.ProductName == LegacyName);
        }
    }
}
