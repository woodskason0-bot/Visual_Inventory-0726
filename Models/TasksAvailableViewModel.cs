using System.Collections.Generic;
using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Models.ViewModels
{
    // Tasks Available page. Orders = "Go Get These", StoreTasks = "Go Store These".
    public class TasksAvailableViewModel
    {
        public List<PendingOrderViewModel> Orders { get; set; } = new();
        public List<VisTask> StoreTasks { get; set; } = new();
    }
}
