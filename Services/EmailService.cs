using System.Net.Mail;
using Visual_Inventory_System.Models;
using Visual_Inventory_System.Data;

namespace Visual_Inventory_System.Services
{
    public class EmailService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public EmailService(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        /// <summary>Emails the team's supervisor when an item crosses down through its threshold.</summary>
        public void CheckAndSendStockAlert(InventoryItem item, int oldQty, int newQty)
        {
            // Only on the way down through the threshold. Already below = already told.
            if (oldQty > item.AlertThreshold && newQty <= item.AlertThreshold)
            {
                // To: this team's supervisor.
                var route = _db.EmailRoutings.FirstOrDefault(r => r.Group == item.Group && r.Team == item.Team);

                if (route == null || string.IsNullOrWhiteSpace(route.SupervisorEmail))
                    return; // No routing rules set up for this team yet

                // CC: the manager and the group's other supervisors.
                var peerEmails = _db.EmailRoutings
                    .Where(r => r.Group == item.Group && r.Team != item.Team)
                    .Select(r => r.SupervisorEmail)
                    .ToList();

                peerEmails.Add(route.ManagerEmail); // Add the boss to the CC list

                var cleanCcList = peerEmails.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
                string ccString = string.Join(",", cleanCcList);

                string subject = $"[Stock Alert] {item.Team} - {item.ItemName} is running low!";
                string body = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px; border: 1px solid #ccc; border-radius: 8px;'>
                        <h2 style='color: #D22027;'>Low Stock Alert</h2>
                        <p><strong>Item:</strong> {item.ItemName} (ID: {item.ItemId})</p>
                        <p><strong>Location:</strong> {item.FdaString}</p>
                        <hr/>
                        <p><strong>Current Quantity:</strong> <span style='color: red; font-weight: bold;'>{newQty}</span></p>
                        <p><strong>Alert Threshold:</strong> {item.AlertThreshold}</p>
                        <br/>
                        <p>Please log into the Visual Inventory System to review.</p>
                    </div>";

                SendEmail(route.SupervisorEmail, ccString, subject, body);
            }
        }

        /// <summary>Emails the supervisors and managers of every team with an item on the order.</summary>
        public void SendOrderSubmittedEmail(Order order, List<InventoryItem> items)
        {
            var involvedTeams = items.Select(i => i.Team).Distinct().ToList();

            var routes = _db.EmailRoutings.Where(r => involvedTeams.Contains(r.Team)).ToList();
            var allEmails = routes.Select(r => r.SupervisorEmail).Concat(routes.Select(r => r.ManagerEmail))
                                  .Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();

            if (!allEmails.Any()) return;

            string primaryTo = allEmails.First();
            string ccString = string.Join(",", allEmails.Skip(1));

            string subject = $"[New Order] Order #{order.Id} Submitted for Pickup";
            string body = $"<h2>New Order #{order.Id}</h2><ul>";

            foreach (var item in items)
            {
                var orderItem = order.Items.First(oi => oi.ItemId == item.ItemId);
                body += $"<li><strong>{item.ItemName}</strong> - Qty: {orderItem.Quantity} (Loc: {item.FdaString})</li>";
            }
            body += "</ul>";

            SendEmail(primaryTo, ccString, subject, body);
        }

        private void SendEmail(string to, string cc, string subject, string body)
        {
            try
            {
                string server = _config["EmailSettings:SmtpServer"] ?? "";
                int port = _config.GetValue<int>("EmailSettings:SmtpPort", 25);
                string from = _config["EmailSettings:FromAddress"] ?? "noreply@localhost";

                // No real SMTP server configured: print to the console instead of sending.
                if (string.IsNullOrWhiteSpace(server) ||
                    server.Contains("dummy", StringComparison.OrdinalIgnoreCase) ||
                    server.Contains("example", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n[DIAGNOSTIC - EMAIL INTERCEPTED]");
                    Console.WriteLine($"TO: {to} | CC: {cc}");
                    Console.WriteLine($"SUBJECT: {subject}");
                    Console.WriteLine("------------------------------------------");
                    return; // Prevent actual crash while we use dummy settings
                }

                using var client = new SmtpClient(server, port);
                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(from),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(to);
                if (!string.IsNullOrWhiteSpace(cc)) mailMessage.CC.Add(cc);

                client.Send(mailMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EMAIL FAILED] {ex.Message}");
            }
        }
    }
}