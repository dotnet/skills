var builder = WebApplication.CreateBuilder(args); var app = builder.Build(); app.MapRazorComponents<App>(); app.Run();
