using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Visual_Inventory_System.Data;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Ends dated holds after their last day and sends the reserver's "ends today"
    /// and "ended" notifications (Kason's rule 7, 2026-11-08). Runs once right after
    /// startup and every 10 minutes while the app is up -- the host is always on, and
    /// every query already treats a hold past its date as not holding, so a missed run
    /// only delays the log row and the notification, never the stock.
    ///
    /// A failure here (no database yet, a locked file) is written to the console and
    /// retried next round; it must never take the app down.
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
