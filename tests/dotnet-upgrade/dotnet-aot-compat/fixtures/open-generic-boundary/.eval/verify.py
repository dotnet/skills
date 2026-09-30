from verify_support import build, read, require, require_sha256

require_sha256("OpenGenericBoundary.csproj", "9bfbf1b1a84eb2b18639521387d83b60a9438e59394601d5abe6d4b238d2ec41")

source = read("RuntimeGenericFactory.cs")
require("RequiresDynamicCode" in source, "The JIT requirement is not exposed.")
require("RequiresUnreferencedCode" in source, "The trimming limitation is not exposed.")
require("UnconditionalSuppressMessage" not in source, "The incompatibility was hidden.")
require("#pragma warning disable" not in source, "The warning was hidden with pragma.")
build("OpenGenericBoundary.csproj")
