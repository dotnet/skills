using Common;
using DataAccess;

namespace BusinessLogic;

public class EntityService
{
    private readonly Repository _repository = new();

    public Result<Entity> CreateEntity(string name)
    {
        var entity = new Entity { Name = name.Truncate(100) };
        return _repository.Add(entity);
    }

    public Result<Entity> GetEntity(int id) => _repository.GetById(id);

    public List<Entity> ListEntities() => _repository.GetAll();

    public Result<string> GetEntitySummary(int id)
    {
        var result = _repository.GetById(id);
        if (!result.Success)
            return Result<string>.Fail(result.Error!);

        var entity = result.Data!;
        return Result<string>.Ok($"{entity.Name} (created {entity.CreatedAt:yyyy-MM-dd})");
    }
}
