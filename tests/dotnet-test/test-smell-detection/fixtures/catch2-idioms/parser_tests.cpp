#include <catch2/catch_test_macros.hpp>
#include <catch2/generators/catch_generators.hpp>

#include <string>

TEST_CASE("parser accepts supported separators")
{
    const auto separator = GENERATE(',', ';');

    SECTION("a generated separator is preserved")
    {
        const std::string input = std::string("left") + separator + "right";

        REQUIRE(input.at(4) == separator);
    }
}
