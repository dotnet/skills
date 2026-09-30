"""Require generated classic tests to detect bounded production regressions."""

from dataclasses import dataclass
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


PINNED_PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);NU1701</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.5.2" />
    <PackageReference Include="MSTest.TestFramework" Version="3.5.2" />
    <PackageReference Include="Moq" Version="4.2.1510.2205" />
    <PackageReference Include="NBuilder" Version="6.1.0" />
    <PackageReference Include="System.CodeDom" Version="8.0.0" />
    <PackageReference Include="System.Security.Permissions" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="../src/DiscountService.cs" />
    <Compile Include="../src/TieredDiscountPolicy.cs" />
    <Compile Include="../tests/FixtureBase.cs" />
    <Compile Include="../tests/DiscountServiceTests.cs" />
    <Compile Include="../tests/DiscountServiceBoundaryTests.cs" />
    <Compile Include="../tests/TieredDiscountPolicyTests.cs" />
  </ItemGroup>
</Project>
"""


@dataclass(frozen=True)
class Mutation:
    name: str
    file: str
    before: str
    after: str


MUTATIONS = (
    Mutation("discount.percentage-validation", "DiscountService.cs",
             "if (percentage < 0m || percentage > 100m)", "if (false)"),
    Mutation("discount.missing-product", "DiscountService.cs",
             "if (product == null)", "if (false)"),
    Mutation("discount.calculation", "DiscountService.cs",
             "return product.Price * (1m - (percentage / 100m));", "return product.Price;"),
    Mutation("tier.threshold-validation", "TieredDiscountPolicy.cs",
             "if (threshold <= 0m)", "if (false)"),
    Mutation("tier.rate-validation", "TieredDiscountPolicy.cs",
             "if (rate < 0m || rate > 1m)", "if (false)"),
    Mutation("tier.subtotal-validation", "TieredDiscountPolicy.cs",
             "if (subtotal < 0m)", "if (false)"),
    Mutation("tier.exact-threshold", "TieredDiscountPolicy.cs",
             "subtotal >= _threshold", "subtotal > _threshold"),
    Mutation("tier.discount", "TieredDiscountPolicy.cs",
             "subtotal * (1m - _rate)", "subtotal"),
)


def run_tests(project):
    result = subprocess.run(
        ["dotnet", "test", str(project), "--no-restore", "--verbosity", "minimal"],
        cwd=project.parent, capture_output=True, text=True, timeout=120,
    )
    return result.returncode, result.stdout + result.stderr


def positive_count(label, output):
    match = re.search(rf"\b{label}:\s*([1-9][0-9]*)\b", output, re.IGNORECASE)
    return int(match.group(1)) if match else 0


def verify(root):
    root = Path(root).resolve()
    validation = root / ".eval-validation" / "GeneratedTests.csproj"
    validation.parent.mkdir(exist_ok=True)
    validation.write_text(PINNED_PROJECT, encoding="utf-8")
    subprocess.run(
        ["dotnet", "build", str(validation), "--verbosity", "minimal"],
        cwd=validation.parent, check=True, capture_output=True, text=True, timeout=120,
    )
    validation.write_text(PINNED_PROJECT.replace("4.2.1510.2205", "4.20.72"), encoding="utf-8")
    subprocess.run(
        ["dotnet", "restore", str(validation)],
        cwd=validation.parent, check=True, capture_output=True, text=True, timeout=120,
    )
    code, output = run_tests(validation)
    if code != 0 or positive_count("Passed", output) == 0:
        raise ValueError(f"Original generated tests must pass:\n{output}")
    with tempfile.TemporaryDirectory(prefix="classic-behavior-") as directory:
        work = Path(directory) / "classic"
        shutil.copytree(root, work)
        project = work / ".eval-validation" / "GeneratedTests.csproj"
        for mutation in MUTATIONS:
            source = work / "src" / mutation.file
            original = source.read_text(encoding="utf-8-sig")
            if original.count(mutation.before) != 1:
                raise ValueError(f"Fixture drift: {mutation.name}")
            try:
                source.write_text(original.replace(mutation.before, mutation.after), encoding="utf-8")
                code, output = run_tests(project)
                if code == 0 or positive_count("Failed", output) == 0:
                    raise ValueError(f"Generated tests did not detect {mutation.name}")
            finally:
                source.write_text(original, encoding="utf-8")
            print(f"Detected {mutation.name}")


if __name__ == "__main__":
    import sys
    verify(sys.argv[1])
