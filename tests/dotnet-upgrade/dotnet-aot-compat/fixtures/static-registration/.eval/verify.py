from verify_support import build_and_run, read, require, require_sha256

require_sha256("Commands.cs", "382bd3e70b611d63478ac863c6d212320a2a31182d4a1a35ac0a56d44fe75633")
require_sha256("Program.cs", "1b64a395b466270d5cbd153198a81f58e0cf87fb9cac834aa18ea0d8fc270f73")
require_sha256("StaticRegistration.csproj", "d81f521c2c56beaf0d2cceb5feadb63e920f013f4b17cccc48d4bb0c6cf3b61b")

source = read("CommandRegistry.cs")
require("Activator.CreateInstance" not in source, "Reflection activation remains.")
require(".GetTypes(" not in source, "Assembly scanning remains.")
require("UnconditionalSuppressMessage" not in source, "The warning was suppressed.")
require("#pragma warning disable" not in source, "The warning was hidden with pragma.")
require("new AddCommand" in source and "new MultiplyCommand" in source, "Known commands are not rooted.")
build_and_run("StaticRegistration.csproj")
