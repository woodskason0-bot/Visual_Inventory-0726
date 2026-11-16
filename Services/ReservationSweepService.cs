using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Visual_Inventory_System.Data;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Ends dated holds after their last day and sends the "ends today" and "ended" notices.
    /// Runs at startup and every 10 minutes. A failure is logged and retried, never fatal.
    /// </summary>
    public class ReservationSweepService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

        public ReservationSweepService(IServiceScopeFactory scopes)
        {
            _scopes = scopes;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    int changed = ReservationService.Sweep(db, DateTime.Today);
                    if (changed > 0)
                        Console.WriteLine($">>> Reservation sweep: {changed} hold(s) ended or warned.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(">>> Reservation sweep failed (will retry): " + ex.GetBaseException().Message);
                }

                try { await Task.Delay(Every, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
    }
}
