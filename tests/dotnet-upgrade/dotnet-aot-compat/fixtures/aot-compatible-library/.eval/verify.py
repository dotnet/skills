from verify_support import build, read, require, require_sha256

require_sha256("Slug.cs", "4c875a2b50467024c05850b75bca5d75f2c6ad5c88bc718af28f06baebcbf7c4")

project = read("AotCompatibleLibrary.csproj")
require("<IsAotCompatible>true</IsAotCompatible>" in project, "The stronger compatibility contract is missing.")
require("<IsTrimmable" not in project, "The redundant narrower property remains.")
build("AotCompatibleLibrary.csproj")
