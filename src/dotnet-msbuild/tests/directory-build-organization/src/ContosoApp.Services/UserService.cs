using ContosoApp.Core;

namespace ContosoApp.Services;

public class UserService
{
    private readonly IRepository<object> _repository;

    public UserService(IRepository<object> repository)
    {
        _repository = repository;
    }

    public Task<object?> GetUserAsync(int id) => _repository.GetByIdAsync(id);
}
