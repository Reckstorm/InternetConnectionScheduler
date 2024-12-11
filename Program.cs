using InternetConnectionScheduler;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService();

var host = builder.Build();
host.Run();
