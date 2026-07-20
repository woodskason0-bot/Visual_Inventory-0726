namespace Visual_Inventory_System.Models
{
    public class EmailRouting
    {
        // EF Core sees "Id" (int) and automatically makes this PK, AI, and NN!
        public int Id { get; set; }

        // EF Core sees 'string' with a default value and makes this TEXT and NN (Not Null)
        public string Group { get; set; } = "";
        public string Team { get; set; } = "";

        public string ManagerName { get; set; } = "";
        public string ManagerEmail { get; set; } = "";

        public string SupervisorName { get; set; } = "";
        public string SupervisorEmail { get; set; } = "";
    }
}