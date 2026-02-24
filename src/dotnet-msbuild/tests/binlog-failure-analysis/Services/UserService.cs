using Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Services;

public class UserService
{
    public Result<string> SerializeUser(User user)
    {
        try
        {
            var json = JsonConvert.SerializeObject(user, Formatting.Indented);
            return Result<string>.Ok(json);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(ex.Message);
        }
    }

    public Result<JObject> ParseUserJson(string json)
    {
        try
        {
            var obj = JObject.Parse(json);
            return Result<JObject>.Ok(obj);
        }
        catch (Exception ex)
        {
            return Result<JObject>.Fail(ex.Message);
        }
    }

    public Result<User> DeserializeUser(string json)
    {
        try
        {
            var user = JsonConvert.DeserializeObject<User>(json);
            if (user == null)
                return Result<User>.Fail("Deserialization returned null");
            return Result<User>.Ok(user);
        }
        catch (Exception ex)
        {
            return Result<User>.Fail(ex.Message);
        }
    }
}
