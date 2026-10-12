namespace SF6Access.Services;

/// <summary>
/// Country name for a CFN "Home" id — the flag shown next to a player's name
/// (VSInfoOffline PlayerData.mHomeId, FighterProfileDesc.HomeId).
///
/// The game's home table (ResidentCFNTableData.CfnHome) has no name message on its
/// records, and its ids follow the UN member list in alphabetical order, with the
/// official names of the time ("Swaziland", "The former Yugoslav Republic of
/// Macedonia"). The localized country names are messages CfnHomeMessage1..199 in a
/// different order (Japan, United States, Canada first, then A to Z). This table
/// links the two; the name itself still comes from the game, so it follows the game
/// language. Built 2026-10-12 and checked id by id against the continent the home
/// table gives each record (homeCategoryId → CfnHomeMessage200..205).
/// </summary>
public static class HomeCountryResolver
{
    // MessageOf[homeId] = N of "CfnHomeMessageN". The table has no id 169 and the
    // Change Flag dialog does not offer it; it sits where Taiwan falls alphabetically,
    // so it maps to Taiwan (message 174) in case an old profile still carries it.
    private static readonly int[] MessageOf =
    {
        0,
        4, 5, 6, 7, 8, 9, 10, 11, 12, 13,                   // 1-10   Afghanistan .. Austria
        14, 15, 16, 17, 18, 19, 20, 21, 22, 23,             // 11-20  Azerbaijan .. Bhutan
        24, 25, 26, 27, 28, 29, 30, 31, 34, 32,             // 21-30  Bolivia .. Cabo Verde, Cambodia
        33, 3, 35, 36, 37, 38, 39, 40, 41, 44,              // 31-40  Cameroon, Canada .. Costa Rica
        45, 46, 47, 48, 49, 92, 42, 50, 51, 52,             // 41-50  Côte d'Ivoire .. North Korea, DR Congo .. Dominica
        53, 54, 55, 56, 57, 58, 59, 60, 61, 62,             // 51-60  Dominican Republic .. Finland
        63, 64, 65, 66, 67, 68, 69, 70, 71, 72,             // 61-70  France .. Guinea
        73, 74, 75, 76, 78, 79, 80, 81, 84, 82,             // 71-80  Guinea-Bissau .. Iran, Iraq
        83, 85, 86, 87, 1, 88, 89, 90, 91, 94,              // 81-90  Ireland .. Japan .. Kuwait
        95, 96, 97, 98, 99, 100, 101, 102, 103, 104,        // 91-100 Kyrgyzstan .. Luxembourg
        106, 107, 108, 109, 110, 111, 112, 113, 114, 115,   // 101-110 Madagascar .. Mexico
        116, 118, 119, 120, 121, 122, 123, 124, 125, 126,   // 111-120 Micronesia .. Nepal
        127, 128, 129, 130, 131, 132, 133, 135, 136, 137,   // 121-130 Netherlands .. Panama
        138, 139, 140, 141, 142, 143, 144, 93, 117, 145,    // 131-140 Papua New Guinea .. South Korea, Moldova, Romania
        146, 147, 166, 148, 167, 149, 150, 151, 152, 153,   // 141-150 Russia .. Senegal
        154, 155, 156, 157, 158, 159, 160, 161, 162, 163,   // 151-160 Serbia .. South Sudan
        164, 165, 168, 169, 170, 171, 172, 173, 174, 175,   // 161-170 Spain .. Syria, (Taiwan), Tajikistan
        177, 105, 178, 179, 180, 181, 182, 183, 184, 185,   // 171-180 Thailand, Macedonia .. Tuvalu
        186, 187, 188, 189, 176, 2, 190, 191, 192, 194,     // 181-190 Uganda .. Tanzania, United States .. Venezuela
        195, 196, 197, 198,                                 // 191-194 Viet Nam .. Zimbabwe
    };

    // The table's "Other" entry.
    private const int OtherHomeId = 999;
    private const int OtherMessage = 134;

    /// <summary>Localized country name for a home id, or null (0 = not set, CPU).</summary>
    public static string Resolve(int homeId)
    {
        int n = homeId == OtherHomeId ? OtherMessage
              : homeId > 0 && homeId < MessageOf.Length ? MessageOf[homeId] : 0;
        return n > 0 ? FlowHelper.ResolveMessageName($"CfnHomeMessage{n}") : null;
    }

    /// <summary>Country of a CommonFighterBannerInfo (its home_id), or null.</summary>
    public static string FromBanner(REFrameworkNET.ManagedObject bannerInfo) =>
        bannerInfo == null ? null : Resolve(FlowHelper.ReadIntField(bannerInfo, "home_id", 0));
}
