from verify_support import build, read, require, require_sha256

require_sha256("IntentionalScanningBoundary.csproj", "9bfbf1b1a84eb2b18639521387d83b60a9438e59394601d5abe6d4b238d2ec41")

source = read("PluginCatalog.cs")
require("RequiresUnreferencedCode" in source, "The unavoidable trimming boundary is not public.")
require(
    "assembly" in source.lower()
    and any(word in source.lower() for word in ("scan", "discover", "load", "reflect")),
    "The warning message does not explain the boundary.",
)
require("UnconditionalSuppressMessage" not in source, "The dynamic boundary was falsely suppressed.")
require("DynamicallyAccessedMemberTypes.All" not in source, "The whole assembly was broadly rooted.")
require("#pragma warning disable" not in source, "The warning was hidden with pragma.")
build("IntentionalScanningBoundary.csproj")
