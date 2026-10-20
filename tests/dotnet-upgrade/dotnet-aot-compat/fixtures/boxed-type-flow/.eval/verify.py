from verify_support import build_and_run, read, require, require_sha256

require_sha256("BoxedTypeFlow.csproj", "d81f521c2c56beaf0d2cceb5feadb63e920f013f4b17cccc48d4bb0c6cf3b61b")

source = read("RequestDispatcher.cs")
program = read("Program.cs")
require("DynamicallyAccessedMembers" in source, "Missing trimming data-flow annotation.")
require("PublicParameterlessConstructor" in source, "The activation contract is not precise.")
require("(Type)arguments[" not in source, "Type still loses annotation flow through object[].")
require("Dispatch(typeof(EchoHandler)" in program, "The caller still boxes Type in object[].")
require('if (result != "HELLO")' in program, "The behavior assertion was removed.")
require("throw new InvalidOperationException" in program, "The behavior assertion no longer fails.")
require("DynamicallyAccessedMemberTypes.All" not in source, "The annotation preserves too much.")
require("UnconditionalSuppressMessage" not in source, "The warning was suppressed.")
require("#pragma warning disable" not in source, "The warning was hidden with pragma.")
build_and_run("BoxedTypeFlow.csproj")
