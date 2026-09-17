param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        "add-control",
        "layout-container",
        "rename-control",
        "data-binding",
        "custom-control",
        "localization",
        "no-op",
        "nested-layout-clipping",
        "async-ui-refresh",
        "vb-application-events"
    )]
    [string] $Scenario
)

$ErrorActionPreference = "Stop"

function Fail([string] $Message)
{
    Write-Error $Message
    exit 1
}

function Read-Source([string] $Path)
{
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        Fail "Required file is missing: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw
}

function Assert-Matches(
    [string] $Text,
    [string] $Pattern,
    [string] $Message
)
{
    if ($Text -notmatch $Pattern)
    {
        Fail $Message
    }
}

function Assert-NotMatches(
    [string] $Text,
    [string] $Pattern,
    [string] $Message
)
{
    if ($Text -match $Pattern)
    {
        Fail $Message
    }
}

function Get-InitializeComponentBody([string] $Text, [string] $Path)
{
    $signature = [regex]::Match(
        $Text,
        '\bvoid\s+InitializeComponent\s*\(\s*\)\s*\{',
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )
    if (-not $signature.Success)
    {
        Fail "InitializeComponent was not found in $Path"
    }

    $openBrace = $signature.Index + $signature.Length - 1
    $depth = 0
    $inString = $false
    $escaped = $false

    for ($index = $openBrace; $index -lt $Text.Length; $index++)
    {
        $character = $Text[$index]
        if ($inString)
        {
            if ($escaped)
            {
                $escaped = $false
            }
            elseif ($character -eq '\')
            {
                $escaped = $true
            }
            elseif ($character -eq '"')
            {
                $inString = $false
            }

            continue
        }

        if ($character -eq '"')
        {
            $inString = $true
            continue
        }

        if ($character -eq '{')
        {
            $depth++
        }
        elseif ($character -eq '}')
        {
            $depth--
            if ($depth -eq 0)
            {
                return $Text.Substring(
                    $openBrace + 1,
                    $index - $openBrace - 1
                )
            }
        }
    }

    Fail "InitializeComponent has unbalanced braces in $Path"
}

function Test-DesignerSafety
{
    $designerFiles = @(Get-ChildItem -Path . -Filter *.Designer.cs -Recurse -File)
    if ($designerFiles.Count -eq 0)
    {
        Fail "No C# WinForms designer file was found."
    }

    foreach ($designerFile in $designerFiles)
    {
        $text = Get-Content -LiteralPath $designerFile.FullName -Raw
        $body = Get-InitializeComponentBody $text $designerFile.FullName

        Assert-NotMatches $text '=>' "Lambda or expression-bodied code found in $($designerFile.Name)."
        Assert-NotMatches $text '\?\?|\?\.|\?\[|\bnameof\s*\(|\bnew\s*\(\s*\)|=\s*\[' "Modern expression syntax found in $($designerFile.Name)."
        Assert-NotMatches $text '(?m)^\s*(?:private|protected|public|internal)\s+[\w<>\[\]?]+\s+\w+\s*\{\s*(?:get|set)\b' "A property was moved into $($designerFile.Name)."

        $methods = [regex]::Matches(
            $text,
            '(?m)^\s*(?:private|protected|public|internal)\s+(?:(?:static|override|virtual|sealed|async)\s+)*[\w<>\[\]?]+\s+(?<name>\w+)\s*\([^;]*\)\s*\{'
        )
        foreach ($method in $methods)
        {
            if ($method.Groups['name'].Value -notin @('Dispose', 'InitializeComponent'))
            {
                Fail "Logic method '$($method.Groups['name'].Value)' was moved into $($designerFile.Name)."
            }
        }

        Assert-NotMatches $body '\b(if|for|foreach|while|goto|switch|try|catch|lock|await)\b' "Control flow found inside InitializeComponent in $($designerFile.Name)."
        Assert-NotMatches $body '(?m)^\s*(?:var|Button|Label|TextBox|ComboBox|ListBox|ListView|TreeView|Panel|GroupBox|TableLayoutPanel|FlowLayoutPanel|BindingSource|PictureBox|DataGridView|StatusStrip|ToolStrip|MenuStrip|TabControl|TabPage|SplitContainer|StatusBadge)\s+\w+\s*=' "A control or component is stored in a local variable inside InitializeComponent in $($designerFile.Name)."
    }
}

function Test-VbDesignerSafety
{
    $designerFiles = @(Get-ChildItem -Path . -Filter *.Designer.vb -Recurse -File)
    if ($designerFiles.Count -eq 0)
    {
        Fail "No Visual Basic WinForms designer file was found."
    }

    $hasInitializeComponent = $false
    foreach ($designerFile in $designerFiles)
    {
        $text = Get-Content -LiteralPath $designerFile.FullName -Raw
        if ($text -match '\bSub\s+InitializeComponent\s*\(\s*\)')
        {
            $hasInitializeComponent = $true
        }
        elseif ($designerFile.Name -ne 'Application.Designer.vb')
        {
            Fail "InitializeComponent was not found in $($designerFile.Name)."
        }
        Assert-NotMatches $text '\bAsync\b|\bAwait\b|\bTry\b|\bCatch\b|Sub\s*\(' "Executable logic was added to $($designerFile.Name)."

        $methods = [regex]::Matches(
            $text,
            '(?im)^\s*(?:Private|Protected|Public|Friend)\s+(?:(?:Overrides|Overridable|Shared)\s+)*Sub\s+(?<name>\w+)\s*\('
        )
        foreach ($method in $methods)
        {
            if ($method.Groups['name'].Value -notin @('New', 'Dispose', 'InitializeComponent', 'OnCreateMainForm'))
            {
                Fail "Logic method '$($method.Groups['name'].Value)' was moved into $($designerFile.Name)."
            }
        }
    }

    if (-not $hasInitializeComponent)
    {
        Fail "No Visual Basic InitializeComponent method was found."
    }
}

if ($Scenario -eq "vb-application-events")
{
    Test-VbDesignerSafety
}
else
{
    Test-DesignerSafety
}

$designer = if ($Scenario -eq "vb-application-events") { Read-Source "MainForm.Designer.vb" } else { Read-Source "MainForm.Designer.cs" }
$codeBehind = if ($Scenario -eq "vb-application-events") { Read-Source "MainForm.vb" } else { Read-Source "MainForm.cs" }

switch ($Scenario)
{
    "add-control"
    {
        Assert-Matches $designer 'private\s+Button\s+_clearButton\s*;' "The clear button is not a designer field."
        Assert-Matches $designer '_clearButton\s*=\s*new\s+Button\s*\(\s*\)\s*;' "The clear button is not instantiated in InitializeComponent."
        Assert-Matches $designer '_clearButton\.Click\s*\+=\s*ClearButton_Click\s*;' "The clear button is not wired to a named handler."
        Assert-Matches $designer 'Controls\.Add\s*\(\s*_clearButton\s*\)' "The clear button is not added to the form."
        Assert-Matches $codeBehind 'void\s+ClearButton_Click\s*\(\s*object\??\s+\w+\s*,\s*EventArgs\s+\w+\s*\)' "ClearButton_Click is not in the code-behind."
        Assert-Matches $codeBehind '(_customerNameTextBox\.Clear\s*\(\s*\)|_customerNameTextBox\.Text\s*=\s*(string\.Empty|""))' "The clear handler does not clear the customer name."
    }
    "layout-container"
    {
        Assert-Matches $designer 'private\s+TableLayoutPanel\s+_detailsLayout\s*;' "The responsive layout is not represented by a designer field named _detailsLayout."
        Assert-Matches $designer '_detailsLayout\s*=\s*new\s+TableLayoutPanel\s*\(\s*\)\s*;' "The table layout is not instantiated in InitializeComponent."
        Assert-Matches $designer '_detailsLayout\.Dock\s*=\s*DockStyle\.Fill\s*;' "The table layout does not fill the form."
        Assert-Matches $designer 'ColumnStyle\s*\(\s*SizeType\.AutoSize' "The caption column is not AutoSize."
        Assert-Matches $designer 'ColumnStyle\s*\(\s*SizeType\.Percent\s*,\s*100F?\s*\)' "The editor column is not percentage-sized."
        foreach ($control in @('_firstNameLabel', '_firstNameTextBox', '_lastNameLabel', '_lastNameTextBox', '_saveButton'))
        {
            Assert-Matches $designer "_detailsLayout\.Controls\.Add\s*\(\s*$control\b" "$control was not moved into the table layout."
        }
        Assert-Matches $designer 'Controls\.Add\s*\(\s*_detailsLayout\s*\)' "The table layout is not added to the form."
    }
    "rename-control"
    {
        $allSource = (Get-ChildItem -Path . -Filter *.cs -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
        Assert-NotMatches $allSource '_saveButton|SaveButton_Click' "The old control or handler name remains."
        Assert-Matches $designer 'private\s+Button\s+_commitButton\s*;' "The renamed control field is missing."
        Assert-Matches $designer '_commitButton\.Click\s*\+=\s*CommitButton_Click\s*;' "The renamed control is not wired to the renamed handler."
        Assert-Matches $codeBehind 'void\s+CommitButton_Click\s*\(' "The renamed handler is missing from code-behind."
    }
    "data-binding"
    {
        Assert-Matches $designer 'components\s*=\s*new\s+(?:System\.ComponentModel\.)?Container\s*\(\s*\)\s*;' "The components container is not initialized."
        Assert-Matches $designer 'private\s+BindingSource\s+_customerViewModelBindingSource\s*;' "The BindingSource is not a designer field."
        Assert-Matches $designer '_customerViewModelBindingSource\s*=\s*new\s+BindingSource\s*\(\s*components\s*\)\s*;' "The BindingSource is not owned by the components container."
        Assert-Matches $designer '_customerViewModelBindingSource\.DataSource\s*=\s*typeof\s*\(\s*CustomerViewModel\s*\)\s*;' "The BindingSource type is not available to the designer."
        Assert-Matches $designer '_nameTextBox\.DataBindings\.Add\s*\(\s*new\s+Binding\s*\(\s*"Text"\s*,\s*_customerViewModelBindingSource\s*,\s*"Name"' "The TextBox is not bound to CustomerViewModel.Name."
        $viewModel = Read-Source "CustomerViewModel.cs"
        Assert-Matches $viewModel '\bINotifyPropertyChanged\b' "CustomerViewModel no longer supports change notification."
    }
    "custom-control"
    {
        $control = Read-Source "StatusBadge.cs"
        Assert-Matches $control '\[\s*DefaultValue\s*\(\s*typeof\s*\(\s*Color\s*\)\s*,\s*"Yellow"\s*\)\s*\]' "HighlightColor does not declare its yellow default."
        Assert-Matches $control '\bColor\s+HighlightColor\s*\{' "HighlightColor is missing."
        Assert-Matches $control '\[\s*DesignerSerializationVisibility\s*\(\s*DesignerSerializationVisibility\.Hidden\s*\)\s*\]' "RuntimeMessages is not hidden from designer serialization."
        Assert-Matches $control '\b(?:List<string>|IList<string>|IReadOnlyList<string>)\s+RuntimeMessages\s*\{' "RuntimeMessages is missing."
        Assert-Matches $control '\bFont\??\s+CustomFont\s*\{' "CustomFont is missing."
        Assert-Matches $control '\bbool\s+ShouldSerializeCustomFont\s*\(\s*\)' "ShouldSerializeCustomFont is missing."
        Assert-Matches $control '\bvoid\s+ResetCustomFont\s*\(\s*\)' "ResetCustomFont is missing."
        Assert-NotMatches $designer 'ShouldSerializeCustomFont|ResetCustomFont|RuntimeMessages' "Custom-control serialization logic was placed in the form designer."
    }
    "localization"
    {
        $resourcePath = "MainForm.resx"
        $resourceText = Read-Source $resourcePath
        try
        {
            [xml] $resourceXml = $resourceText
        }
        catch
        {
            Fail "MainForm.resx is not valid XML."
        }

        $resourceNames = @($resourceXml.root.data | ForEach-Object { $_.name })
        if ('$this.Text' -notin $resourceNames -or '_saveButton.Text' -notin $resourceNames)
        {
            Fail "MainForm.resx must contain `$this.Text and _saveButton.Text entries."
        }

        Assert-Matches $designer 'ComponentResourceManager\s+\w+\s*=\s*new\s+ComponentResourceManager\s*\(\s*typeof\s*\(\s*MainForm\s*\)\s*\)\s*;' "The designer does not create a ComponentResourceManager for MainForm."
        Assert-Matches $designer '\.ApplyResources\s*\(\s*_saveButton\s*,\s*"_saveButton"\s*\)' "The save button is not localized through ApplyResources."
        Assert-Matches $designer '\.ApplyResources\s*\(\s*this\s*,\s*"\$this"\s*\)' "The form title is not localized through ApplyResources."
        Assert-NotMatches $designer '_saveButton\.Text\s*=\s*"Save"|Text\s*=\s*"Customer Editor"' "Hard-coded UI text remains in the designer."
    }
    "no-op"
    {
        $expected = Read-Source "expected-hashes.txt"
        foreach ($line in ($expected -split '\r?\n'))
        {
            if ([string]::IsNullOrWhiteSpace($line))
            {
                continue
            }

            if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64})\s{2}(?<path>.+)$')
            {
                Fail "Invalid expected hash entry: $line"
            }

            $source = (Read-Source $Matches['path']) -replace "`r`n", "`n"
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($source)
            $actual = [Convert]::ToHexString(
                [System.Security.Cryptography.SHA256]::HashData($bytes)
            )
            if ($actual -ne $Matches['hash'])
            {
                Fail "$($Matches['path']) changed even though the designer was already safe."
            }
        }
    }
    "nested-layout-clipping"
    {
        Assert-Matches $designer '(?m)^\s*AutoSize\s*=\s*true\s*;' "The form is not configured to size from its nested content."
        Assert-Matches $designer '(?m)^\s*AutoSizeMode\s*=\s*AutoSizeMode\.GrowAndShrink\s*;' "The form does not grow and shrink with its content."
        foreach ($container in @('_contentLayout', '_addressGroup', '_addressLayout'))
        {
            Assert-Matches $designer "$container\.AutoSize\s*=\s*true\s*;" "$container is still fixed-size."
            Assert-Matches $designer "$container\.AutoSizeMode\s*=\s*AutoSizeMode\.GrowAndShrink\s*;" "$container does not grow and shrink with its content."
        }
        Assert-Matches $designer '_contentLayout\.Dock\s*=\s*DockStyle\.Top\s*;' "The outer content layout is not attached to the top of the form."
        Assert-Matches $designer '_addressLayout\.Dock\s*=\s*DockStyle\.Top\s*;' "The inner address layout is not attached to the top of its group."
        Assert-Matches $designer '_contentLayout\.Controls\.Add\s*\(\s*_addressGroup\s*\)' "The address group no longer participates in the outer layout."
        Assert-Matches $designer '_addressGroup\.Controls\.Add\s*\(\s*_addressLayout\s*\)' "The address layout no longer participates in the group layout."
        foreach ($control in @('_streetLabel', '_streetTextBox', '_cityLabel', '_cityTextBox', '_postalCodeLabel', '_postalCodeTextBox'))
        {
            Assert-Matches $designer "_addressLayout\.Controls\.Add\s*\(\s*$control\b" "$control was moved out of the address layout."
        }
        Assert-NotMatches $designer '_addressGroup\.Size\s*=|_addressLayout\.Size\s*=|_contentLayout\.Size\s*=' "A nested container still has an explicit fixed size."
    }
    "async-ui-refresh"
    {
        Assert-Matches $designer '_refreshButton\.Click\s*\+=\s*RefreshButton_Click\s*;' "The Refresh button is not wired to its named handler."
        Assert-Matches $codeBehind '\basync\s+void\s+RefreshButton_Click\s*\(' "The event handler does not await its asynchronous work."
        Assert-Matches $codeBehind '\bawait\s+Task\.Run\s*\(' "The background refresh is not awaited."
        Assert-Matches $codeBehind '\bawait\s+(?:(?:this|_statusLabel)\.)?InvokeAsync\s*\(' "The UI update is not marshaled with an awaited operation."
        Assert-Matches $codeBehind 'catch\s*\(\s*OperationCanceledException\b' "Cancellation is not handled separately."
        Assert-Matches $codeBehind 'catch\s*\(\s*Exception\b' "Unexpected refresh failures are not handled."
        Assert-Matches $codeBehind 'finally\s*\{' "The Refresh button state is not restored from a finally block."
        Assert-Matches $codeBehind '_refreshButton\.Enabled\s*=\s*true\s*;' "The Refresh button is not re-enabled."
        Assert-NotMatches $codeBehind '_\s*=\s*Task\.Run|\.BeginInvoke\s*\(' "Fire-and-forget work remains in the refresh path."
        Assert-NotMatches $designer '\bTask\b|\bOperationCanceledException\b|\bException\b' "Asynchronous or error-handling logic was placed in the designer file."
    }
    "vb-application-events"
    {
        $applicationEvents = Read-Source "My Project\ApplicationEvents.vb"
        $allVbSource = (Get-ChildItem -Path . -Filter *.vb -Recurse -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
        Assert-NotMatches $allVbSource '(?im)^\s*(?:Public|Private|Friend)?\s*(?:Shared\s+)?Sub\s+Main\s*\(' "A second application entry point was added."
        Assert-Matches $applicationEvents '(?is)\bSub\s+\w+\s*\([^)]*StartupNextInstanceEventArgs[^)]*\).*?Handles\s+Me\.StartupNextInstance' "ApplicationEvents.vb does not handle repeated launches."
        Assert-Matches $applicationEvents '(?is)\bMainForm\.WindowState\s*=\s*FormWindowState\.Normal\b' "A minimized MainForm is not restored."
        Assert-Matches $applicationEvents '(?is)\bMainForm\.(?:Activate|BringToFront)\s*\(\s*\)' "MainForm is not activated after a repeated launch."
        Assert-Matches $applicationEvents '(?is)\bSub\s+\w+\s*\([^)]*UnhandledExceptionEventArgs[^)]*\).*?Handles\s+Me\.UnhandledException' "ApplicationEvents.vb does not handle otherwise-unhandled UI exceptions."
        Assert-Matches $applicationEvents '(?is)\bFile\.AppendAllText\s*\(\s*"application-errors\.log"\s*,.*?\bException\b' "The unhandled exception is not appended to application-errors.log."
        Assert-Matches $applicationEvents '\bExitApplication\s*=\s*True\b' "The unhandled-exception path does not explicitly exit."

        $expected = Read-Source "expected-hashes.txt"
        foreach ($line in ($expected -split '\r?\n'))
        {
            if ([string]::IsNullOrWhiteSpace($line))
            {
                continue
            }

            if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64})\s{2}(?<path>.+)$')
            {
                Fail "Invalid expected hash entry: $line"
            }

            $source = (Read-Source $Matches['path']) -replace "`r`n", "`n"
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($source)
            $actual = [Convert]::ToHexString(
                [System.Security.Cryptography.SHA256]::HashData($bytes)
            )
            if ($actual -ne $Matches['hash'])
            {
                Fail "$($Matches['path']) changed even though the existing framework startup configuration must be preserved."
            }
        }
    }
}

Write-Host "WinForms outcome and designer-safety validation passed for '$Scenario'."
