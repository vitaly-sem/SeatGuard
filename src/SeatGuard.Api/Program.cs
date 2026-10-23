var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "SeatGuard", status = "booting" }));

app.Run();

public partial class Program;
