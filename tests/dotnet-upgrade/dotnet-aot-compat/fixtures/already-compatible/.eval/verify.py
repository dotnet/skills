from verify_support import build_and_run, require_sha256

require_sha256("AlreadyCompatible.csproj", "d81f521c2c56beaf0d2cceb5feadb63e920f013f4b17cccc48d4bb0c6cf3b61b")
require_sha256("Program.cs", "9eeeecb0ca5688badfb2cea6fd3808ed287ffab70459b9596b59d6f64dd7d63c")

build_and_run("AlreadyCompatible.csproj")
