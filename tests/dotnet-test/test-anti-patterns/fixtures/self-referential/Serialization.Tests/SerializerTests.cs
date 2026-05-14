using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Serialization.Tests;

[TestClass]
public class SerializerTests
{
    [TestMethod]
    public void Serialize_ThenDeserialize_ProducesOriginal()
    {
        var input = new UserDto { Name = "Alice", Age = 30 };
        var json = Serializer.ToJson(input);
        var output = Serializer.FromJson<UserDto>(json);
        Assert.AreEqual(input.Name, output.Name);
        Assert.AreEqual(input.Age, output.Age);
    }

    [TestMethod]
    public void ToString_ParseBack_Identity()
    {
        var value = new Money(42.50m, "USD");
        var text = value.ToString();
        var parsed = Money.Parse(text);
        Assert.AreEqual(value, parsed);
    }

    [TestMethod]
    public void Clone_ProducesEqualObject()
    {
        var original = new Config { Timeout = 30, Retries = 3 };
        var clone = original.Clone();
        Assert.AreEqual(original.Timeout, clone.Timeout);
        Assert.AreEqual(original.Retries, clone.Retries);
    }

    [TestMethod]
    public void NormalizeName_AlreadyNormalized_NoChange()
    {
        var name = "alice";
        var result = NameNormalizer.Normalize(name);
        Assert.AreEqual(name, result);
    }

    [TestMethod]
    public void MapToDto_MapBack_RoundTrip()
    {
        var entity = new UserEntity { Id = 1, FullName = "Alice Smith", Email = "a@b.com" };
        var dto = Mapper.ToDto(entity);
        var backToEntity = Mapper.ToEntity(dto);
        Assert.AreEqual(entity.Id, backToEntity.Id);
        Assert.AreEqual(entity.FullName, backToEntity.FullName);
        Assert.AreEqual(entity.Email, backToEntity.Email);
    }

    [TestMethod]
    public void GetDisplayName_ReturnsFormattedName()
    {
        var user = new UserDto { Name = "Alice", Age = 30 };
        var display = user.GetDisplayName();
        Assert.AreEqual(user.Name, display);
    }

    [TestMethod]
    public void Validate_ValidInput_ReturnsSameInput()
    {
        var input = "hello@world.com";
        var result = Validator.ValidateEmail(input);
        Assert.AreEqual(input, result);
    }

    [TestMethod]
    public void UpperCase_ToUpper_Works()
    {
        var input = "HELLO";
        var result = input.ToUpper();
        Assert.AreEqual(input, result);
    }
}
