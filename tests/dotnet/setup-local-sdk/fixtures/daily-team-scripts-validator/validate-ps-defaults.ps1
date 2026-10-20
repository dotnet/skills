# Deterministic checks of the generated PowerShell team script, through the PowerShell parser:
#   1. it parses
#   2. it defaults the workload list to ios and android at the workload install call (the Workloads parameter default, or a
#      reachable constant assignment to $Workloads before the call), so that running it without arguments installs both
#   3. every native call whose failure must stop the script is followed by a $LASTEXITCODE check that
#      tests for failure (not an inverted condition) and throws or exits non-zero: the downloaded
#      dotnet-install script, workload config, workload install, build-server shutdown
#      ($ErrorActionPreference = 'Stop' does not make a native exit code terminating)
#   4. the workload install call passes $Workloads, so the defaults reach the installer
#   5. the native calls target the downloaded installer script and the local .dotnet SDK, not the system dotnet;
#      a failed workload install still shuts the build servers down before the failure is acted on
#   6. the calls and the guards sit on a reachable path: code under a constant-false condition, or inside a function that no reachable
#      call invokes, does not count; a function called from the top-level flow counts
#   7. the local dotnet is the project-local one as of each call: the path names .dotnet, or a variable whose last reachable assignment
#      before the call is a dotnet path under '.dotnet'; the installer variable must have been downloaded to, from an official
#      dotnet-install.ps1 address, before it is invoked
#   8. the installer arguments, resolved as of the call through the splatted array (= resets, += appends) and the constant variables
#      at that point, are -Channel 12.0.1xx -Quality daily -InstallDir .dotnet, with no exact -Version pin
#   9. a reachable workload-list call is followed by a per-workload band check that throws on mismatch
$ErrorActionPreference = 'Stop'
$candidate = Get-ChildItem -Path . -File | Where-Object { $_.Name -like 'install*.ps1' } | Select-Object -First 1
if (-not $candidate) { Write-Host 'FAIL  no install*.ps1 in the workspace'; exit 1 }
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($candidate.FullName, [ref]$tokens, [ref]$errors)
if ($errors) { $errors | ForEach-Object { Write-Host "FAIL  $($candidate.Name): $($_.Message)" }; exit 1 }
$failures = 0
function Get-ConstantValues($expr) {
    if (-not $expr) { return @() }
    try { return @($expr.SafeGetValue()) } catch { return @() }
}
function Test-HasBoth($values) {
    # only string constants count: with a boolean on the left, -contains would coerce 'ios' to $true
    $strings = @($values | Where-Object { $_ -is [string] })
    return (($strings -contains 'ios') -and ($strings -contains 'android'))
}
function Test-DirectPath($node, $block) {
    $p = $node.Parent
    while ($p -and -not [object]::ReferenceEquals($p, $block)) {
        if ($p -is [System.Management.Automation.Language.IfStatementAst] -or $p -is [System.Management.Automation.Language.SwitchStatementAst] -or $p -is [System.Management.Automation.Language.LoopStatementAst] -or
            $p -is [System.Management.Automation.Language.TryStatementAst] -or $p -is [System.Management.Automation.Language.ScriptBlockExpressionAst] -or $p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return $false }
        $p = $p.Parent
    }
    return [bool]$p
}
function Get-VariableName($expr) {
    while ($expr -is [System.Management.Automation.Language.ConvertExpressionAst] -or $expr -is [System.Management.Automation.Language.AttributedExpressionAst]) { $expr = $expr.Child }
    if ($expr -is [System.Management.Automation.Language.VariableExpressionAst]) { return $expr.VariablePath.UserPath }
    return ''
}
# 3. failure propagation: the statement right after each native call must look at $LASTEXITCODE, either
#    `if ($LASTEXITCODE ...) { throw/exit }` or `$x = $LASTEXITCODE` with a later `if (... $x ...) { throw/exit }`
function Get-EnclosingStatement($node) {
    $stmt = $node
    while ($stmt -and -not (($stmt.Parent -is [System.Management.Automation.Language.StatementBlockAst]) -or ($stmt.Parent -is [System.Management.Automation.Language.NamedBlockAst]))) { $stmt = $stmt.Parent }
    return $stmt
}
function Get-NextStatement($node) {
    $stmt = Get-EnclosingStatement $node
    if (-not $stmt) { return $null }
    $siblings = @($stmt.Parent.Statements)
    for ($i = 0; $i -lt $siblings.Count; $i++) {
        if ([object]::ReferenceEquals($siblings[$i], $stmt)) { if ($i + 1 -lt $siblings.Count) { return $siblings[$i + 1] } else { return $null } }
    }
    return $null
}
function Test-TryFinallyShutdown($node) {
    $parent = $node.Parent
    while ($parent) {
        if ($parent -is [System.Management.Automation.Language.TryStatementAst]) {
            if (-not $parent.Finally) { return $false }
            return [bool]($parent.Finally.FindAll({
                $args[0] -is [System.Management.Automation.Language.CommandAst] -and
                (Get-NativeArguments $args[0]) -match '^build-server\s+shutdown\b'
            }, $true) | Select-Object -First 1)
        }
        $parent = $parent.Parent
    }
    return $false
}
function Test-Guard($ifAst, $varName) {
    # condition: a genuine non-zero test of the variable (truthy, -ne 0, 0 -ne, -not (... -eq 0)); -gt 0 misses the negative codes
    # Windows reports for native failures with the high bit set, and -ge 0 is true on success
    $cond = (($ifAst.Clauses | ForEach-Object { $_.Item1.Extent.Text }) -join ' ').Trim()
    $v = [regex]::Escape('$' + $varName)
    $condOk = $cond -match ('^(' + $v + '|' + $v + '\s+-ne\s+0|0\s+-ne\s+' + $v + '|(-not|!)\s*\(?\s*' + $v + '\s+-eq\s+0\s*\)?)$')
    # the first terminator on the guard's direct path, in source order, decides: a throw, or an exit with a literal non-zero code or the
    # tested variable, passes; a bare exit, exit 0, or a return before any throw fails (`exit 0; throw` exits 0). Terminators nested under
    # another condition, loop, try, script block or function do not count
    $bodyOk = $false
    foreach ($blk in ($ifAst.Clauses | ForEach-Object { $_.Item2 })) {
        $terminators = @($blk.FindAll({ $args[0] -is [System.Management.Automation.Language.ThrowStatementAst] -or $args[0] -is [System.Management.Automation.Language.ExitStatementAst] -or $args[0] -is [System.Management.Automation.Language.ReturnStatementAst] }, $true) |
            Where-Object { Test-DirectPath $_ $blk } | Sort-Object { $_.Extent.StartOffset })
        if ($terminators.Count -eq 0) { continue }
        $first = $terminators[0]
        if ($first -is [System.Management.Automation.Language.ThrowStatementAst]) { $bodyOk = $true; continue }
        if ($first -is [System.Management.Automation.Language.ExitStatementAst] -and $first.Pipeline) {
            $expr = $first.Pipeline.GetPureExpression()
            if ($expr -is [System.Management.Automation.Language.ConstantExpressionAst] -and $expr.Value -is [int] -and $expr.Value -ne 0) { $bodyOk = $true }
            elseif ($expr -is [System.Management.Automation.Language.VariableExpressionAst] -and $expr.VariablePath.UserPath -eq $varName) { $bodyOk = $true }
        }
    }
    return ($condOk -and $bodyOk)
}
function Test-Reachable($node, $depth = 0) {
    # a call under `if ($false) { ... }`, `if (0)`, `while ($false)` or in the else of `if ($true)` never runs; constant conditions are evaluated
    function Get-ConstantTruth($pipeline) {
        if (-not $pipeline) { return $null }
        $expr = $pipeline.GetPureExpression(); if (-not $expr) { return $null }
        if ($expr -is [System.Management.Automation.Language.VariableExpressionAst]) {
            switch ($expr.VariablePath.UserPath.ToLowerInvariant()) { 'true' { return $true } 'false' { return $false } 'null' { return $false } default { return $null } }
        }
        if ($expr -is [System.Management.Automation.Language.ConstantExpressionAst]) { try { return [bool]$expr.SafeGetValue() } catch { return $null } }
        return $null
    }
    $child = $node; $parent = $node.Parent
    while ($parent) {
        if ($parent -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
            # a function body runs when some reachable call invokes it (from outside its own body); an uninvoked function never runs
            if ($depth -ge 5) { return $false }
            $fname = $parent.Name; $def = $parent
            if ($def.Parent -and -not (Test-Reachable $def ($depth + 1))) { return $false }  # a definition under if ($false) never happens
            $calls = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.CommandAst] -and $args[0].GetCommandName() -eq $fname }, $true) |
                Where-Object { -not ($_.Extent.StartOffset -ge $def.Extent.StartOffset -and $_.Extent.EndOffset -le $def.Extent.EndOffset) })
            foreach ($call in $calls) { if (Test-Reachable $call ($depth + 1)) { return $true } }
            return $false
        }
        if ($parent -is [System.Management.Automation.Language.IfStatementAst]) {
            $inElse = $parent.ElseClause -and $parent.ElseClause.Extent.StartOffset -le $child.Extent.StartOffset -and $child.Extent.EndOffset -le $parent.ElseClause.Extent.EndOffset
            if ($inElse) {
                foreach ($c in $parent.Clauses) { if ((Get-ConstantTruth $c.Item1) -eq $true) { return $false } }
            } else {
                # the clause that holds the node must not be constant-false, and no earlier clause may be constant-true
                # (`if ($true) { } elseif ($x) { <node> }` never reaches the node)
                foreach ($c in $parent.Clauses) {
                    $holds = $c.Item2.Extent.StartOffset -le $child.Extent.StartOffset -and $child.Extent.EndOffset -le $c.Item2.Extent.EndOffset
                    if ($holds) { if ((Get-ConstantTruth $c.Item1) -eq $false) { return $false }; break }
                    if ((Get-ConstantTruth $c.Item1) -eq $true) { return $false }
                }
            }
        } elseif ($parent -is [System.Management.Automation.Language.WhileStatementAst] -or $parent -is [System.Management.Automation.Language.DoWhileStatementAst]) {
            if ((Get-ConstantTruth $parent.Condition) -eq $false -and $parent -is [System.Management.Automation.Language.WhileStatementAst]) { return $false }
        }
        $child = $parent; $parent = $parent.Parent
    }
    return $true
}
function Test-LocalPathText($text, $offset) {
    # a path is project-local when it names .dotnet literally, or through a variable whose last reachable constant assignment before
    # $offset is '.dotnet' (./.dotnet, .\.dotnet)
    if ($text -match '(^|[\\/"'' ])\.dotnet([\\/"'']|$)') { return $true }
    foreach ($m in [regex]::Matches($text, '\$\{?(\w+)\}?')) {
        $val = Resolve-Constant $m.Groups[1].Value $offset
        if ($val -is [string] -and $val -match '^(\./|\.\\)?\.dotnet[\\/]?$') { return $true }
    }
    return $false
}
function Resolve-Constant($name, $offset) {
    # the constant string a variable holds at $offset: the last reachable `=` assignment that ends before it; $null when none or not constant
    $last = $null
    foreach ($as in ($allAssignments | Where-Object { (Get-VariableName $_.Left) -eq $name -and $_.Operator -eq [System.Management.Automation.Language.TokenKind]::Equals -and $_.Extent.EndOffset -le $offset -and (Test-Reachable $_) } | Sort-Object { $_.Extent.StartOffset })) { $last = $as }
    if (-not $last) { return $null }
    $right = $last.Right; if ($right -is [System.Management.Automation.Language.CommandExpressionAst]) { $right = $right.Expression }
    try { $v = $right.SafeGetValue() } catch { return $null }
    if ($v -is [string]) { return $v } else { return $null }
}
function Resolve-ArrayElements($expr, $offset) {
    # flatten an array literal into tokens; constant strings stay, string variables resolve, anything else becomes '<?>'
    $items = @()
    if ($expr -is [System.Management.Automation.Language.ArrayExpressionAst]) { $items = @($expr.SubExpression.Statements | ForEach-Object { if ($_ -is [System.Management.Automation.Language.PipelineAst]) { $_.GetPureExpression() } else { $_ } }) }
    elseif ($expr -is [System.Management.Automation.Language.ArrayLiteralAst]) { $items = @($expr.Elements) }
    else { $items = @($expr) }
    $out = @()
    foreach ($it in $items) {
        if ($it -is [System.Management.Automation.Language.ArrayLiteralAst]) { $out += Resolve-ArrayElements $it $offset; continue }
        if ($it -is [System.Management.Automation.Language.StringConstantExpressionAst]) { $out += $it.Value; continue }
        if ($it -is [System.Management.Automation.Language.VariableExpressionAst]) { $v = Resolve-Constant $it.VariablePath.UserPath $offset; $out += $(if ($null -ne $v) { $v } else { '<?>' }); continue }
        $out += '<?>'
    }
    return $out
}
function Resolve-CommandTokens($cmd) {
    # the arguments a command receives as of its position: direct elements, and for each splatted variable its reachable assignments that
    # end before the call, in source order, with `=` resetting the array and `+=` appending; later assignments do not count
    $offset = $cmd.Extent.StartOffset
    $out = @()
    foreach ($el in @($cmd.CommandElements | Select-Object -Skip 1)) {
        if ($el -is [System.Management.Automation.Language.CommandParameterAst]) { $out += ('-' + $el.ParameterName); if ($el.Argument) { $out += Resolve-ArrayElements $el.Argument $offset }; continue }
        if ($el -is [System.Management.Automation.Language.VariableExpressionAst] -and $el.Splatted) {
            $name = $el.VariablePath.UserPath
            $splat = @()
            foreach ($as in ($allAssignments | Where-Object { (Get-VariableName $_.Left) -eq $name -and $_.Extent.EndOffset -le $offset -and (Test-Reachable $_) } | Sort-Object { $_.Extent.StartOffset })) {
                $right = $as.Right; if ($right -is [System.Management.Automation.Language.CommandExpressionAst]) { $right = $right.Expression }
                if ($as.Operator -eq [System.Management.Automation.Language.TokenKind]::Equals) { $splat = @() }
                $splat += Resolve-ArrayElements $right $as.Extent.StartOffset
            }
            $out += $splat
            continue
        }
        $out += Resolve-ArrayElements $el $offset
    }
    return $out
}
function Get-ArgValue($tokens, $name) {
    for ($i = 0; $i -lt $tokens.Count; $i++) { if ("$($tokens[$i])" -ieq "-$name" -and $i + 1 -lt $tokens.Count) { return "$($tokens[$i + 1])" } }
    return $null
}
function Get-NativeArguments($cmd) {
    # only a real invocation counts: `& <downloaded installer variable> ...` or `& <dotnet(.exe) under the project-local .dotnet> ...`;
    # `Write-Host 'workload config'`, `& 'dotnet' ...` (the system SDK), `& "$installDir\dotnet.exe"` with $installDir = '/usr/share/dotnet'
    # and `& $other @installArgs` return $null
    if ($cmd.InvocationOperator -ne [System.Management.Automation.Language.TokenKind]::Ampersand) { return $null }
    $first = $cmd.CommandElements[0]
    $offset = $cmd.Extent.StartOffset
    # the installer: a variable that a reachable Invoke-WebRequest -OutFile wrote to before this call
    $isInstaller = $first -is [System.Management.Automation.Language.VariableExpressionAst] -and [bool]($downloads | Where-Object { $_.Name -eq $first.VariablePath.UserPath -and $_.End -le $offset })
    $isLocalDotnet = ($first -is [System.Management.Automation.Language.ExpandableStringExpressionAst] -or $first -is [System.Management.Automation.Language.StringConstantExpressionAst]) -and
        $first.Extent.Text -match 'dotnet(\.exe)?["'']?$' -and (Test-LocalPathText $first.Extent.Text $offset)
    if (-not $isLocalDotnet -and -not $isInstaller -and $first -is [System.Management.Automation.Language.VariableExpressionAst]) {
        # `$dotnet = Join-Path $installDir 'dotnet.exe'; & $dotnet ...`: the variable's last reachable assignment before this call must be a
        # dotnet path under the local SDK; a later reassignment does not rescue an earlier system-SDK call
        $name = $first.VariablePath.UserPath
        $last = $null
        foreach ($as in ($allAssignments | Where-Object { (Get-VariableName $_.Left) -eq $name -and $_.Extent.EndOffset -le $offset -and (Test-Reachable $_) } | Sort-Object { $_.Extent.StartOffset })) { $last = $as }
        $isLocalDotnet = [bool]($last -and $last.Right.Extent.Text -match 'dotnet' -and (Test-LocalPathText $last.Right.Extent.Text $last.Extent.StartOffset))
    }
    if (-not ($isInstaller -or $isLocalDotnet)) { return $null }
    return (@($cmd.CommandElements | Select-Object -Skip 1 | ForEach-Object { $_.Extent.Text }) -join ' ')
}
# the AST indexes the checks below share; built before the download scan, which resolves -Uri variables through them
$ifs = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.IfStatementAst] }, $true))
$commands = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.CommandAst] }, $true))
$allAssignments = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))
# the variables that reachable Invoke-WebRequest -OutFile calls wrote the installer to, with where each download ends: only
# `& <such a variable>` after its download counts as running the downloaded script
$downloads = @()
foreach ($iwr in @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.CommandAst] -and $args[0].GetCommandName() -match '^(Invoke-WebRequest|iwr|curl|wget)$' }, $true) | Where-Object { Test-Reachable $_ })) {
    $els = @($iwr.CommandElements); $outVar = $null; $uri = $null
    for ($i = 0; $i -lt $els.Count - 1; $i++) {
        if ($els[$i] -is [System.Management.Automation.Language.CommandParameterAst] -and $els[$i].ParameterName -match '^OutFile$|^o$') { $outVar = Get-VariableName $els[$i + 1] }
        if ($els[$i] -is [System.Management.Automation.Language.CommandParameterAst] -and $els[$i].ParameterName -match '^Uri$|^u$') {
            $u = $els[$i + 1]
            if ($u -is [System.Management.Automation.Language.StringConstantExpressionAst]) { $uri = $u.Value }
            elseif ($u -is [System.Management.Automation.Language.VariableExpressionAst]) { $uri = Resolve-Constant $u.VariablePath.UserPath $iwr.Extent.StartOffset }
        }
    }
    # only a download from an official dotnet-install.ps1 address counts
    if ($outVar -and $uri -match '^https://(dot\.net/v1|dotnet\.microsoft\.com/download/dotnet/scripts/v1|builds\.dotnet\.microsoft\.com/dotnet/scripts/v1)/dotnet-install\.ps1$') { $downloads += [pscustomobject]@{ Name = $outVar; End = $iwr.Extent.EndOffset } }
}
$required = @(
    @{ name = 'dotnet-install script'; pattern = '@installArgs|-InstallDir' },
    @{ name = 'workload config';       pattern = '^workload\s+config\s+--update-mode\s+manifests\b' },
    @{ name = 'workload install';      pattern = '^workload\s+install\b' },
    @{ name = 'build-server shutdown'; pattern = '^build-server\s+shutdown\b' }
)
foreach ($r in $required) {
    $cmd = $commands | Where-Object { $a = Get-NativeArguments $_; $null -ne $a -and $a -match $r.pattern -and (Test-Reachable $_) } | Select-Object -First 1
    if (-not $cmd) { Write-Host "FAIL  no $($r.name) call found"; $failures++; continue }
    if ($r.name -eq 'dotnet-install script') {
        # resolve the installer arguments statically: direct `-Name value` pairs plus the splatted array's reachable assignments, with
        # string variables resolved to their single constant value; the call must install the 12.0.1xx channel, daily quality, into .dotnet
        $tokens = @(Resolve-CommandTokens $cmd)
        $argsOk = (Get-ArgValue $tokens 'InstallDir') -match '^(\./|\.\\)?\.dotnet[\\/]?$' -and (Get-ArgValue $tokens 'Channel') -eq '12.0.1xx' -and (Get-ArgValue $tokens 'Quality') -eq 'daily' -and -not (Get-ArgValue $tokens 'Version')
        if ($argsOk) { Write-Host 'ok    dotnet-install: -Channel 12.0.1xx -Quality daily -InstallDir .dotnet' } else { Write-Host "FAIL  dotnet-install arguments resolve to [$($tokens -join ' ')], expected -Channel 12.0.1xx -Quality daily -InstallDir .dotnet"; $failures++ }
    }
    if ($r.name -eq 'workload install') {
        # 2. default workloads, judged at this call: the Workloads parameter default, or a reachable constant assignment to $Workloads
        #    that ends before the call; an assignment after the call or under a constant-false condition does not count
        # the last reachable assignment to $Workloads before the call decides; the parameter default applies only when nothing supersedes it
        $found = $false
        $lastAssign = $null
        foreach ($a in ($allAssignments | Where-Object { (Get-VariableName $_.Left) -eq 'Workloads' -and $_.Extent.EndOffset -le $cmd.Extent.StartOffset -and (Test-Reachable $_) } | Sort-Object { $_.Extent.StartOffset })) { $lastAssign = $a }
        if ($lastAssign) {
            $right = $lastAssign.Right
            if ($right -is [System.Management.Automation.Language.CommandExpressionAst]) { $right = $right.Expression }
            $found = Test-HasBoth (Get-ConstantValues $right)
        } elseif ($ast.ParamBlock) {
            foreach ($p in $ast.ParamBlock.Parameters) {
                if ($p.Name.VariablePath.UserPath -eq 'Workloads' -and $p.DefaultValue -and (Test-HasBoth (Get-ConstantValues $p.DefaultValue))) { $found = $true }
            }
        }
        if ($found) { Write-Host "ok    $($candidate.Name) defaults `$Workloads to ios and android at the install call" }
        else { Write-Host "FAIL  $($candidate.Name): neither the Workloads parameter default nor a reachable assignment to `$Workloads before the install call holds both ios and android"; $failures++ }
        $usesWorkloads = [bool]($cmd.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.VariableExpressionAst] -and $_.VariablePath.UserPath -eq 'Workloads' })
        if ($usesWorkloads) { Write-Host 'ok    workload install receives $Workloads' } else { Write-Host "FAIL  workload install does not pass `$Workloads ($($cmd.Extent.Text))"; $failures++ }
    }
    $nextStmt = Get-NextStatement $cmd
    $next = if ($nextStmt) { $nextStmt.Extent.Text } else { '' }
    $ok = $false
    if ($nextStmt -is [System.Management.Automation.Language.IfStatementAst]) {
        if ($r.name -eq 'workload install') {
            if (Test-TryFinallyShutdown $cmd) {
                $ok = Test-Guard $nextStmt 'LASTEXITCODE'
            } else {
                Write-Host "FAIL  workload install: an immediate guard must be protected by a finally block that shuts down build servers"; $failures++; continue
            }
        } else {
            $ok = Test-Guard $nextStmt 'LASTEXITCODE'
        }
    } elseif ($next -match '^\s*\$(\w+)\s*=\s*\$LASTEXITCODE\s*$') {
        # the guard must come after the capture and inside the block that holds it
        $var = $Matches[1]
        $block = $nextStmt.Parent
        # any write to the captured variable between the capture and a guard voids that guard ($rc = $LASTEXITCODE; $rc = 0; if ($rc) ...)
        $writes = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true) | Where-Object { (Get-VariableName $_.Left) -eq $var -and $_.Extent.StartOffset -gt $nextStmt.Extent.EndOffset })
        $guardAst = $ifs | Where-Object {
            $guard = $_
            $guard.Extent.StartOffset -gt $nextStmt.Extent.EndOffset -and $guard.Extent.EndOffset -le $block.Extent.EndOffset -and (Test-Guard $guard $var) -and (Test-Reachable $guard) -and
            -not ($writes | Where-Object { $_.Extent.StartOffset -lt $guard.Extent.StartOffset }) } | Select-Object -First 1
        $ok = [bool]$guardAst
        if ($ok -and $r.name -eq 'workload install') {
            # build-server shutdown must run between the capture and the first guard, so a failed install still releases the nodes
            $shutdown = $commands | Where-Object { $a = Get-NativeArguments $_; $null -ne $a -and $a -match '^build-server\s+shutdown\b' -and $_.Extent.StartOffset -gt $nextStmt.Extent.EndOffset -and $_.Extent.EndOffset -lt $guardAst.Extent.StartOffset -and (Test-Reachable $_) } | Select-Object -First 1
            if (-not $shutdown) { Write-Host "FAIL  workload install: no build-server shutdown between the exit-code capture and the guard"; $failures++; continue }
        }
    }
    if ($ok) { Write-Host "ok    $($r.name): failure stops the script" } else { Write-Host "FAIL  $($r.name): no guard that tests a non-zero exit code and throws or exits non-zero right after it ($next)"; $failures++ }
}
$installCall = $commands | Where-Object { (Get-NativeArguments $_) -match '^workload\s+install\b' -and (Test-Reachable $_) } | Select-Object -First 1
$listCall = $commands | Where-Object { (Get-NativeArguments $_) -match '^workload\s+list\b' -and (Test-Reachable $_) -and (!$installCall -or $_.Extent.StartOffset -gt $installCall.Extent.EndOffset) } | Select-Object -First 1
if (-not $listCall) {
    Write-Host 'FAIL  no reachable workload list call after workload install'; $failures++
} else {
    $listAssignment = $listCall
    while ($listAssignment -and -not ($listAssignment -is [System.Management.Automation.Language.AssignmentStatementAst])) { $listAssignment = $listAssignment.Parent }
    $listVar = if ($listAssignment) { Get-VariableName $listAssignment.Left } else { '' }
    $bandLoops = @()
    if ($listVar) {
        foreach ($loop in @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.ForEachStatementAst] }, $true))) {
            if (-not (Test-Reachable $loop) -or $loop.Extent.StartOffset -le $listCall.Extent.EndOffset -or $loop.Condition.Extent.Text -notmatch '\$Workloads\b') { continue }
            $itemVar = $loop.Variable.VariablePath.UserPath
            $versionAssignment = $loop.Body.FindAll({ $args[0] -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true) | Where-Object {
                $_.Right.Extent.Text -match ('\$' + [regex]::Escape($listVar) + '\b') -and
                $_.Right.Extent.Text -match ('\$' + [regex]::Escape($itemVar) + '\b')
            } | Select-Object -First 1
            if (-not $versionAssignment) { continue }
            $versionVar = Get-VariableName $versionAssignment.Left
            $guard = $loop.Body.FindAll({ $args[0] -is [System.Management.Automation.Language.IfStatementAst] }, $true) | Where-Object {
                $condition = (($_.Clauses | ForEach-Object { $_.Item1.Extent.Text }) -join ' ')
                $condition -match ('\$' + [regex]::Escape($versionVar) + '\b') -and
                $condition -match '\$band\b' -and
                $_.FindAll({ $args[0] -is [System.Management.Automation.Language.ThrowStatementAst] }, $true).Count -gt 0
            } | Select-Object -First 1
            if ($guard) { $bandLoops += $loop }
        }
    }
    if ($bandLoops.Count -gt 0) { Write-Host 'ok    workload list data is checked for every requested workload band' }
    else { Write-Host 'FAIL  workload list is not followed by a per-workload band mismatch guard'; $failures++ }
}
if ($failures -eq 0) { Write-Host 'all checks passed'; exit 0 }
Write-Host "$failures check(s) failed"; exit 1
