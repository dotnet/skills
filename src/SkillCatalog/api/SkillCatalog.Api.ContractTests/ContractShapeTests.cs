namespace SkillCatalog.Api.ContractTests;
public sealed class ContractShapeTests
{
    [Fact] public void Expected_routes_are_declared_in_program_source()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../api/SkillCatalog.Api/Program.cs")));
        Assert.Contains("MapCatalogEndpoints", source); Assert.Contains("MapSkillEndpoints", source); Assert.Contains("MapOpenApi", source);
    }
}
