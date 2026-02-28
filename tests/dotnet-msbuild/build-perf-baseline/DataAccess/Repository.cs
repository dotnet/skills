using Common;

namespace DataAccess;

public class Repository
{
    private readonly List<Entity> _store = new();

    public Result<Entity> Add(Entity entity)
    {
        if (string.IsNullOrEmpty(entity.Name))
            return Result<Entity>.Fail("Name is required");

        entity.Id = _store.Count + 1;
        _store.Add(entity);
        return Result<Entity>.Ok(entity);
    }

    public Result<Entity> GetById(int id)
    {
        var entity = _store.FirstOrDefault(e => e.Id == id);
        return entity is not null
            ? Result<Entity>.Ok(entity)
            : Result<Entity>.Fail($"Entity {id} not found");
    }

    public List<Entity> GetAll() => _store.ToList();
}
