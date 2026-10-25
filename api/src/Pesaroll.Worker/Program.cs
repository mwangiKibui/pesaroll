using Pesaroll.Notifications;
using Pesaroll.Organizations;
using Pesaroll.Payroll;
using Pesaroll.Shared.Jobs;

// The worker (PLAT-04): runs the jobs the API queues and the recurring ones (reminders, from NTF-01
// and ONB-06) and pay run calculations (PAY-03), from the queue in PostgreSQL. Nothing here serves requests.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Pesaroll")
    ?? throw new InvalidOperationException("ConnectionStrings:Pesaroll is not set.");

builder.Services.AddPesarollJobs(connectionString, runJobs: true);
builder.Services.AddNotificationsModule(connectionString, builder.Configuration, builder.Environment.IsProduction());
builder.Services.AddOrganizationsModule(connectionString);
// Works out pay runs the API queues (PAY-03).
builder.Services.AddPayrollModule(connectionString);
// Reminders to run payroll (ONB-06), every hour.
builder.Services.AddRecurringJob<PayrollRemindersJob>();

var host = builder.Build();
host.Run();
