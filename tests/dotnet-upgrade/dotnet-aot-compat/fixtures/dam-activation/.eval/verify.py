from verify_support import build_and_run, read, require, require_sha256

require_sha256("Program.cs", "bf9f28501a402a29ba4ce14ce2543f179025b4798a58165d08881574f7ad3209")
require_sha256("DamActivation.csproj", "d81f521c2c56beaf0d2cceb5feadb63e920f013f4b17cccc48d4bb0c6cf3b61b")

source = read("PluginFactory.cs")
require("DynamicallyAccessedMembers" in source, "Missing trimming data-flow annotation.")
require("PublicParameterlessConstructor" in source, "The constructor requirement is not precise.")
require("DynamicallyAccessedMemberTypes.All" not in source, "The annotation preserves too much.")
require("UnconditionalSuppressMessage" not in source, "The warning was suppressed instead of modeled.")
require("RequiresUnreferencedCode" not in source, "An analyzable requirement was made opaque.")
require("#pragma warning disable" not in source, "The warning was hidden with pragma.")
build_and_run("DamActivation.csproj")
