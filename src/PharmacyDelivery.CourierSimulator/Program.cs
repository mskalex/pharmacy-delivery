using PharmacyDelivery.CourierSimulator;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCourierSimulator(builder.Configuration, builder.Logging);
await builder.Build().RunAsync();
