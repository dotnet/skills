"""Require generated classic tests to detect bounded production regressions."""

from dataclasses import dataclass
from pathlib import Path
import shutil
import subprocess
import tempfile


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


def verify(root):
    root = Path(root).resolve()
    validation = root / ".eval-validation" / "GeneratedTests.csproj"
    if not validation.is_file():
        raise ValueError("Missing generated classic validation project")
    project_text = validation.read_text(encoding="utf-8")
    if "4.2.1510.2205" in project_text:
        validation.write_text(
            project_text.replace("4.2.1510.2205", "4.20.72"),
            encoding="utf-8",
        )
    subprocess.run(
        ["dotnet", "restore", str(validation)],
        cwd=validation.parent, check=True, capture_output=True, text=True, timeout=120,
    )
    code, output = run_tests(validation)
    if code != 0 or "Passed:" not in output:
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
                if code == 0 or "Failed:" not in output:
                    raise ValueError(f"Generated tests did not detect {mutation.name}")
            finally:
                source.write_text(original, encoding="utf-8")
            print(f"Detected {mutation.name}")


if __name__ == "__main__":
    import sys
    verify(sys.argv[1])
