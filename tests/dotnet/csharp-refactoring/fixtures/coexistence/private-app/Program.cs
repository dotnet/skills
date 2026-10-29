using Reports;

var sales = new[] { 120.50m, 80.00m, 42.25m };
Console.WriteLine(ReportBuilder.Build("October", sales));
