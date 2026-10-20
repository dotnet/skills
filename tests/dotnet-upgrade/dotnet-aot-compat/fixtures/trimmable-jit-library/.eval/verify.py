from verify_support import build, read, require, require_sha256

require_sha256("DynamicProxyFactory.cs", "018e5bd2f61aaf1272acaa184ab64604dfc03778271c171d7db1ae5dcfd5cf31")

project = read("TrimmableJitLibrary.csproj")
require("<IsTrimmable" in project, "Trim analysis is not enabled.")
require("Condition=" in project, "The property is not limited to supported TFMs.")
require("<IsAotCompatible" not in project, "The JIT-only library falsely claims AOT compatibility.")
build("TrimmableJitLibrary.csproj")
