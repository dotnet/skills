using ControllerOrders.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<ICustomerService, CustomerService>();

var app = builder.Build();

app.MapControllers();

app.Run();

public partial class Program;
