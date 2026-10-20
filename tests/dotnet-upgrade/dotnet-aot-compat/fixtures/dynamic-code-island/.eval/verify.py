from verify_support import build_and_run, read, require, require_sha256

require_sha256("Program.cs", "c470c5d0fdc48140d537efb2db64253ce77348e4054364b8ec9f17bcf6996bd6")
require_sha256("DynamicCodeIsland.csproj", "d81f521c2c56beaf0d2cceb5feadb63e920f013f4b17cccc48d4bb0c6cf3b61b")

source = read("FormatterFactory.cs")
require("RuntimeFeature.IsDynamicCodeSupported" in source, "The dynamic path is not gated.")
require("new StaticFormatter" in source, "The AOT-safe fallback is missing.")
if "UnconditionalSuppressMessage" in source:
    require('"AOT"' in source and '"IL3050"' in source, "The suppression is not specific.")
    require("Justification" in source, "The suppression has no invariant-based justification.")
require("#pragma warning disable" not in source, "The warning was hidden with pragma.")
build_and_run("DynamicCodeIsland.csproj")
