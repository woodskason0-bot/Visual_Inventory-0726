using System.Collections.Generic;
using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Models.ViewModels
{
    // Backs the two-section "Tasks Available" page:
    //   Orders     -> "Go Get These" (the pending-order pickup queue)
    //   StoreTasks -> "Go Store These" (claimable sticky-note tasks)
    public class TasksAvailableViewModel
    {
        public List<PendingOrderViewModel> Orders { get; set; } = new();
        public List<VisTask> StoreTasks { get; set; } = new();
    }
}
