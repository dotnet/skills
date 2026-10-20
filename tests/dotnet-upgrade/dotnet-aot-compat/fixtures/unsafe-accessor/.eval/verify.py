from verify_support import build_and_run, read, require, require_sha256

require_sha256("VendorCounter.cs", "02f739adcba2b68d70e919b84558f851c47d58d3174f38cb19f512e233c040f7")
require_sha256("Program.cs", "dfbde32d2fec4e3262cdbd0d05d51825ad4f68c9d8343bc604f2d1ab5bafce50")
require_sha256("UnsafeAccessor.csproj", "d81f521c2c56beaf0d2cceb5feadb63e920f013f4b17cccc48d4bb0c6cf3b61b")

source = read("CounterInterop.cs")
require("UnsafeAccessor" in source, "The known private field does not use a static accessor.")
require("UnsafeAccessorKind.Field" in source, "The accessor kind is not a field.")
require('"_count"' in source, "The accessor does not name the fixed field.")
require(".GetField(" not in source and "FieldInfo" not in source, "Reflection remains.")
require("RequiresUnreferencedCode" not in source, "The warning was propagated instead of removed.")
require("UnconditionalSuppressMessage" not in source, "The warning was suppressed.")
build_and_run("UnsafeAccessor.csproj")
