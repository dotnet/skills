<!-- Test asset: Library project that sets IsAotCompatible but developer
     also manually sets redundant properties, not knowing they cascade.
     Expected: Skill should explain that IsAotCompatible implies the others. -->

<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsAotCompatible>true</IsAotCompatible>

    <!-- These are all redundant — IsAotCompatible implies them -->
    <IsTrimmable>true</IsTrimmable>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>

    <!-- But this is NOT set — should be -->
    <!-- <TrimmerSingleWarn>false</TrimmerSingleWarn> -->
  </PropertyGroup>
</Project>
