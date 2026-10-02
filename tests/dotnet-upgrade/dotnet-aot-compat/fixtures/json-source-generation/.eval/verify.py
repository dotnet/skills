from verify_support import build_and_run, read, require, require_sha256

require_sha256("Program.cs", "bca523ee60f0bc834eb27b86d756f10a7934fabdef26e96c65f1f9e9a69a235a")
require_sha256("JsonSourceGeneration.csproj", "d81f521c2c56beaf0d2cceb5feadb63e920f013f4b17cccc48d4bb0c6cf3b61b")

source = read("MessageSerializer.cs")
all_sources = "\n".join(path.read_text(encoding="utf-8") for path in __import__("pathlib").Path(".").glob("*.cs"))
require("JsonSerializerContext" in all_sources, "No source-generated JSON context was added.")
require("JsonSerializable" in all_sources, "The message type is not registered for generation.")
require("Default.Message" in source, "The serializer does not use generated metadata.")
require("Serialize((object)" not in source, "The reflection-oriented overload remains.")
require("new JsonSerializerOptions" not in source, "Runtime metadata discovery remains configured.")
require("RequiresUnreferencedCode" not in all_sources, "The warning was propagated instead of removed.")
require("UnconditionalSuppressMessage" not in all_sources, "The warning was suppressed.")
require("#pragma warning disable" not in all_sources, "The warning was hidden with pragma.")
build_and_run("JsonSourceGeneration.csproj")
