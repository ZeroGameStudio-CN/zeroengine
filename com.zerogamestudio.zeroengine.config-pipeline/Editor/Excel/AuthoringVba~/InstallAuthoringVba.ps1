param(
    [Parameter(Mandatory = $true)]
    [string[]] $WorkbookPath
)

$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'ZgsAuthoring.bas') -Raw -Encoding UTF8
$workbookSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'ThisWorkbook.cls') -Raw -Encoding UTF8
$actionKeys = @('ADD', 'COPY', 'DELETE', 'RELATION', 'TECHNICAL', 'HELP')
$actionLabels = @(
    '新增',
    '复制',
    '安全删除',
    '编辑关系',
    '技术区',
    '帮助'
)
$excel = $null

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    foreach ($path in $WorkbookPath) {
        $absolute = [System.IO.Path]::GetFullPath($path)
        if (-not [System.IO.File]::Exists($absolute)) {
            throw "Workbook not found: $absolute"
        }

        $workbook = $excel.Workbooks.Open($absolute, 0, $false)
        try {
            $excel.EnableEvents = $false
            foreach ($worksheet in $workbook.Worksheets) {
                if ($worksheet.Visible -ne -1 -or $worksheet.ListObjects.Count -eq 0) {
                    continue
                }

                $worksheet.Unprotect('') | Out-Null
                $firstHeaderRow = 1048576
                foreach ($table in $worksheet.ListObjects) {
                    $firstHeaderRow = [Math]::Min($firstHeaderRow, $table.HeaderRowRange.Row)
                }
                if ($firstHeaderRow -eq 2) {
                    $worksheet.Rows.Item(1).Insert() | Out-Null
                    $firstHeaderRow = 3
                }
                if ($firstHeaderRow -ne 3) {
                    throw "Unsupported authoring header row in $($worksheet.Name): $firstHeaderRow"
                }

                $worksheet.Rows.Item(1).Hidden = $false
                $worksheet.Rows.Item(1).RowHeight = 24
                for ($index = 0; $index -lt $actionLabels.Count; $index++) {
                    $cell = $worksheet.Cells.Item(1, $index + 1)
                    $cell.Value2 = $actionLabels[$index]
                    $cell.Font.Bold = $true
                    $cell.Font.Color = 0xFFFFFF
                    $cell.Interior.Color = 0xD59B5B
                    $cell.HorizontalAlignment = -4108
                    $cell.VerticalAlignment = -4108
                    $token = -join ($worksheet.Name.ToCharArray() | ForEach-Object {
                        if ($_ -match '[A-Za-z0-9_]') { [string]$_ }
                        else { '_{0:X4}' -f [int][char]$_ }
                    })
                    $definedName = "ZGS_ACTION_${token}_$($actionKeys[$index])"
                    try { $workbook.Names.Item($definedName).Delete() | Out-Null } catch { }
                    $workbook.Names.Add($definedName, "='$($worksheet.Name.Replace("'", "''"))'!`$$([char](65 + $index))`$1") | Out-Null
                }

                $worksheet.Activate() | Out-Null
                $excel.ActiveWindow.FreezePanes = $false
                $excel.ActiveWindow.SplitColumn = 0
                $excel.ActiveWindow.SplitRow = 3
                $excel.ActiveWindow.FreezePanes = $true
                $worksheet.Protect(
                    '',
                    $true,
                    $true,
                    $true,
                    $true,
                    $false,
                    $false,
                    $false,
                    $false,
                    $false,
                    $false,
                    $false,
                    $false,
                    $true,
                    $true,
                    $false
                ) | Out-Null
            }

            foreach ($definedName in @($workbook.Names)) {
                $name = $definedName.Name
                if ($name.StartsWith('ZGS_') -and
                    -not $name.StartsWith('ZGS_ENUM_') -and
                    -not $name.StartsWith('ZGS_ACTION_') -and
                    -not $name.StartsWith('ZGS_META_')) {
                    $definedName.Delete() | Out-Null
                }
            }

            try {
                $project = $workbook.VBProject
            }
            catch {
                throw 'Excel denied VBProject access. Enable Trust access to the VBA project object model for this controlled build, then retry.'
            }
            if ($null -eq $project) {
                throw 'Excel denied VBProject access. Enable Trust access to the VBA project object model for this controlled build, then retry.'
            }

            for ($index = $project.VBComponents.Count; $index -ge 1; $index--) {
                $component = $project.VBComponents.Item($index)
                if ($component.Type -ne 100) {
                    $project.VBComponents.Remove($component) | Out-Null
                    continue
                }

                if ($component.Type -eq 100) {
                    $code = $component.CodeModule
                    if ($code.CountOfLines -gt 0) {
                        $code.DeleteLines(1, $code.CountOfLines) | Out-Null
                    }
                }
            }

            $module = $project.VBComponents.Add(1)
            $module.Name = 'ZgsAuthoring'
            $module.CodeModule.AddFromString($moduleSource) | Out-Null
            $thisWorkbook = $project.VBComponents.Item($workbook.CodeName)
            $thisWorkbook.CodeModule.AddFromString($workbookSource) | Out-Null

            $workbook.Activate() | Out-Null
            $missing = [System.Type]::Missing
            $excel.MacroOptions('ZgsShortcutAdd', $missing, $missing, $missing, $true, 'N') | Out-Null
            $excel.MacroOptions('ZgsShortcutCopy', $missing, $missing, $missing, $true, 'C') | Out-Null
            $excel.MacroOptions('ZgsShortcutDelete', $missing, $missing, $missing, $true, 'D') | Out-Null
            $excel.MacroOptions('ZgsShortcutRelation', $missing, $missing, $missing, $true, 'R') | Out-Null
            $excel.MacroOptions('ZgsShortcutTechnical', $missing, $missing, $missing, $true, 'T') | Out-Null
            $excel.MacroOptions('ZgsShortcutHelp', $missing, $missing, $missing, $true, 'H') | Out-Null
            $workbook.Save() | Out-Null
        }
        finally {
            $excel.EnableEvents = $true
            $workbook.Close($false) | Out-Null
        }
    }
}
finally {
    if ($null -ne $excel) {
        $excel.Quit() | Out-Null
    }
}
