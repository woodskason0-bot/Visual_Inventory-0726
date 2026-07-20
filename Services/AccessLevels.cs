namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// The five access tiers, as integers so checks read as "level >= required".
    /// Editing a person's tier is a one-number change in the Users table.
    /// </summary>
    public static class AccessLevels
    {
        public const int Viewer = 1;
        public const int Standard = 2;
        public const int Engineer = 3;
        public const int Management = 4;
        public const int Admin = 5;

        public static string Name(int level) => level switch
        {
            1 => "Viewer",
            2 => "Standard",
            3 => "Engineer",
            4 => "Management",
            5 => "Admin",
            _ => "Unknown"
        };
    }
}
